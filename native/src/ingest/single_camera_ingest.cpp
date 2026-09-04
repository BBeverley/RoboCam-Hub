#include "ingest/single_camera_ingest.h"

#include <cstring>

namespace robocamhub::ingest {
namespace {

constexpr std::size_t maximum_camera_id_length = 255;
constexpr std::size_t maximum_rtsp_url_length = 2048;
constexpr std::uint32_t default_connect_timeout_ms = 10000;
constexpr std::uint32_t minimum_connect_timeout_ms = 100;
constexpr std::uint32_t maximum_connect_timeout_ms = 120000;

bool IsValidUtf8Field(const char* value, std::size_t maximum_length)
{
  return value != nullptr && value[0] != '\0' && std::strlen(value) <= maximum_length
    && g_utf8_validate(value, -1, nullptr) != FALSE;
}

bool IsRtspUrl(const char* value)
{
  return std::strncmp(value, "rtsp://", 7) == 0 || std::strncmp(value, "rtsps://", 8) == 0;
}

}  // namespace

SingleCameraIngest::~SingleCameraIngest()
{
  Stop();
}

rch_result SingleCameraIngest::Configure(const rch_camera_config_v1& config)
{
  const std::scoped_lock lock(control_mutex_);
  const auto current_state = state_.load(std::memory_order_acquire);
  if (current_state != RCH_CAMERA_STATE_STOPPED && current_state != RCH_CAMERA_STATE_FAILED) {
    return RCH_RESULT_INVALID_STATE;
  }

  if (!IsValidUtf8Field(config.camera_id_utf8, maximum_camera_id_length)
      || !IsValidUtf8Field(config.rtsp_url_utf8, maximum_rtsp_url_length)
      || !IsRtspUrl(config.rtsp_url_utf8) || config.reserved != 0) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  const auto timeout_ms = config.connect_timeout_ms == 0
    ? default_connect_timeout_ms
    : config.connect_timeout_ms;
  if (timeout_ms < minimum_connect_timeout_ms || timeout_ms > maximum_connect_timeout_ms) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  if (pipeline_ != nullptr) {
    ResetPipeline();
  }

  camera_id_ = config.camera_id_utf8;
  rtsp_url_ = config.rtsp_url_utf8;
  connect_timeout_ = std::chrono::milliseconds(timeout_ms);
  configured_ = true;
  state_.store(RCH_CAMERA_STATE_STOPPED, std::memory_order_release);
  last_result_.store(RCH_RESULT_OK, std::memory_order_release);
  return RCH_RESULT_OK;
}

rch_result SingleCameraIngest::Start()
{
  const std::scoped_lock lock(control_mutex_);
  const auto current_state = state_.load(std::memory_order_acquire);
  if (current_state == RCH_CAMERA_STATE_STARTING || current_state == RCH_CAMERA_STATE_RECEIVING) {
    return RCH_RESULT_ALREADY_STARTED;
  }
  if (current_state == RCH_CAMERA_STATE_STOPPING) {
    return RCH_RESULT_INVALID_STATE;
  }
  if (!configured_) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  if (pipeline_ != nullptr) {
    ResetPipeline();
  }

  latest_frame_.Clear();
  last_result_.store(RCH_RESULT_OK, std::memory_order_release);
  state_.store(RCH_CAMERA_STATE_STARTING, std::memory_order_release);

  const auto build_result = BuildPipeline();
  if (build_result != RCH_RESULT_OK) {
    SetFailure(build_result);
    ResetPipeline();
    state_.store(RCH_CAMERA_STATE_FAILED, std::memory_order_release);
    return build_result;
  }

  active_session_count_.store(1, std::memory_order_release);
  active_decoder_count_.store(1, std::memory_order_release);
  stop_requested_.store(false, std::memory_order_release);
  start_time_ = std::chrono::steady_clock::now();

  try {
    monitor_thread_ = std::thread(&SingleCameraIngest::MonitorBus, this);
  } catch (...) {
    SetFailure(RCH_RESULT_OUT_OF_MEMORY);
    ResetPipeline();
    state_.store(RCH_CAMERA_STATE_FAILED, std::memory_order_release);
    return RCH_RESULT_OUT_OF_MEMORY;
  }

  const auto state_change = gst_element_set_state(pipeline_, GST_STATE_PLAYING);
  if (state_change == GST_STATE_CHANGE_FAILURE) {
    SetFailure(RCH_RESULT_GSTREAMER_ERROR);
    ResetPipeline();
    state_.store(RCH_CAMERA_STATE_FAILED, std::memory_order_release);
    return RCH_RESULT_GSTREAMER_ERROR;
  }

  return RCH_RESULT_OK;
}

rch_result SingleCameraIngest::Stop()
{
  const std::scoped_lock lock(control_mutex_);
  if (pipeline_ == nullptr) {
    active_session_count_.store(0, std::memory_order_release);
    active_decoder_count_.store(0, std::memory_order_release);
    latest_frame_.Clear();
    last_result_.store(RCH_RESULT_OK, std::memory_order_release);
    state_.store(RCH_CAMERA_STATE_STOPPED, std::memory_order_release);
    return RCH_RESULT_OK;
  }

  state_.store(RCH_CAMERA_STATE_STOPPING, std::memory_order_release);
  ResetPipeline();
  latest_frame_.Clear();
  last_result_.store(RCH_RESULT_OK, std::memory_order_release);
  state_.store(RCH_CAMERA_STATE_STOPPED, std::memory_order_release);
  return RCH_RESULT_OK;
}

void SingleCameraIngest::FillStatus(rch_camera_status_v1& status) const
{
  const auto frame = latest_frame_.Snapshot();
  status.state = state_.load(std::memory_order_acquire);
  status.last_result = last_result_.load(std::memory_order_acquire);
  status.active_rtsp_session_count = active_session_count_.load(std::memory_order_acquire);
  status.active_decoder_count = active_decoder_count_.load(std::memory_order_acquire);
  status.has_latest_frame = frame.has_frame ? 1U : 0U;
  status.latest_frame_width = frame.width;
  status.latest_frame_height = frame.height;
  status.reserved = 0;
  status.decoded_frame_count = frame.frame_count;
  status.latest_frame_sequence = frame.sequence;
  status.latest_frame_timestamp_ns = frame.timestamp_ns;
  status.latest_frame_age_ms = frame.has_frame ? frame.age_ms : RCH_NO_FRAME_AGE_MS;
}

rch_result SingleCameraIngest::BuildPipeline()
{
  auto* pipeline = gst_pipeline_new("robocamhub-camera");
  auto* source = gst_element_factory_make("rtspsrc", "rtsp-source");
  auto* depayloader = gst_element_factory_make("rtph264depay", "h264-depayloader");
  auto* parser = gst_element_factory_make("h264parse", "h264-parser");
  auto* decoder = gst_element_factory_make("avdec_h264", "h264-decoder");
  auto* queue = gst_element_factory_make("queue", "latest-frame-boundary");
  auto* sink = gst_element_factory_make("appsink", "latest-frame-sink");

  if (pipeline == nullptr || source == nullptr || depayloader == nullptr || parser == nullptr
      || decoder == nullptr || queue == nullptr || sink == nullptr) {
    if (pipeline != nullptr) {
      gst_object_unref(pipeline);
    }
    if (source != nullptr) {
      gst_object_unref(source);
    }
    if (depayloader != nullptr) {
      gst_object_unref(depayloader);
    }
    if (parser != nullptr) {
      gst_object_unref(parser);
    }
    if (decoder != nullptr) {
      gst_object_unref(decoder);
    }
    if (queue != nullptr) {
      gst_object_unref(queue);
    }
    if (sink != nullptr) {
      gst_object_unref(sink);
    }
    return RCH_RESULT_GSTREAMER_ERROR;
  }

  g_object_set(source,
               "location", rtsp_url_.c_str(),
               "latency", 0U,
               "drop-on-latency", TRUE,
               nullptr);
  gst_util_set_object_arg(G_OBJECT(source), "buffer-mode", "none");
  gst_util_set_object_arg(G_OBJECT(source), "protocols", "udp");
  g_object_set(decoder, "max-threads", 1, nullptr);
  g_object_set(queue,
               "max-size-buffers", 1U,
               "max-size-bytes", 0U,
               "max-size-time", UINT64_C(0),
               "leaky", 2,
               nullptr);
  g_object_set(sink, "sync", FALSE, "enable-last-sample", FALSE, nullptr);
  gst_app_sink_set_max_buffers(GST_APP_SINK(sink), 1);
  g_object_set(sink, "drop", TRUE, nullptr);
  gst_app_sink_set_wait_on_eos(GST_APP_SINK(sink), FALSE);

  GstAppSinkCallbacks callbacks{};
  callbacks.new_sample = &SingleCameraIngest::OnNewSample;
  gst_app_sink_set_callbacks(GST_APP_SINK(sink), &callbacks, this, nullptr);

  gst_bin_add_many(GST_BIN(pipeline), source, depayloader, parser, decoder, queue, sink, nullptr);
  if (!gst_element_link_many(depayloader, parser, decoder, queue, sink, nullptr)) {
    gst_object_unref(pipeline);
    return RCH_RESULT_GSTREAMER_ERROR;
  }

  g_signal_connect(source, "pad-added", G_CALLBACK(&SingleCameraIngest::OnRtspPadAdded), this);

  pipeline_ = pipeline;
  rtsp_source_ = source;
  depayloader_ = depayloader;
  decoder_ = decoder;
  app_sink_ = sink;
  bus_ = gst_element_get_bus(pipeline);
  return bus_ == nullptr ? RCH_RESULT_GSTREAMER_ERROR : RCH_RESULT_OK;
}

void SingleCameraIngest::MonitorBus()
{
  constexpr auto poll_interval = 50 * GST_MSECOND;
  while (!stop_requested_.load(std::memory_order_acquire)) {
    auto* message = gst_bus_timed_pop_filtered(
      bus_,
      poll_interval,
      static_cast<GstMessageType>(GST_MESSAGE_ERROR | GST_MESSAGE_EOS));

    if (message != nullptr) {
      if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_ERROR) {
        GError* error = nullptr;
        gchar* debug_details = nullptr;
        gst_message_parse_error(message, &error, &debug_details);

        auto result = RCH_RESULT_GSTREAMER_ERROR;
        if (GST_MESSAGE_SRC(message) == GST_OBJECT(decoder_)) {
          result = RCH_RESULT_DECODER_FAILURE;
        } else if (error != nullptr && error->domain == GST_RESOURCE_ERROR) {
          result = RCH_RESULT_RTSP_FAILURE;
        }

        if (error != nullptr) {
          g_error_free(error);
        }
        g_free(debug_details);
        gst_message_unref(message);
        SetFailure(result);
        return;
      }

      gst_message_unref(message);
      SetFailure(RCH_RESULT_RTSP_FAILURE);
      return;
    }

    if (state_.load(std::memory_order_acquire) == RCH_CAMERA_STATE_STARTING
        && std::chrono::steady_clock::now() - start_time_ >= connect_timeout_) {
      SetFailure(RCH_RESULT_CONNECTION_TIMEOUT);
      return;
    }
  }
}

void SingleCameraIngest::SetFailure(rch_result result)
{
  last_result_.store(result, std::memory_order_release);
  if (pipeline_ != nullptr) {
    gst_element_set_state(pipeline_, GST_STATE_NULL);
  }
  latest_frame_.Clear();
  active_session_count_.store(0, std::memory_order_release);
  active_decoder_count_.store(0, std::memory_order_release);
  state_.store(RCH_CAMERA_STATE_FAILED, std::memory_order_release);
}

void SingleCameraIngest::ResetPipeline()
{
  stop_requested_.store(true, std::memory_order_release);
  if (pipeline_ != nullptr) {
    gst_element_set_state(pipeline_, GST_STATE_NULL);
  }
  if (monitor_thread_.joinable()) {
    monitor_thread_.join();
  }
  if (bus_ != nullptr) {
    gst_object_unref(bus_);
  }
  if (pipeline_ != nullptr) {
    gst_object_unref(pipeline_);
  }

  pipeline_ = nullptr;
  rtsp_source_ = nullptr;
  depayloader_ = nullptr;
  decoder_ = nullptr;
  app_sink_ = nullptr;
  bus_ = nullptr;
  active_session_count_.store(0, std::memory_order_release);
  active_decoder_count_.store(0, std::memory_order_release);
}

void SingleCameraIngest::OnRtspPadAdded(GstElement*, GstPad* new_pad, gpointer user_data)
{
  auto* self = static_cast<SingleCameraIngest*>(user_data);
  auto* sink_pad = gst_element_get_static_pad(self->depayloader_, "sink");
  if (sink_pad == nullptr || gst_pad_is_linked(sink_pad)) {
    if (sink_pad != nullptr) {
      gst_object_unref(sink_pad);
    }
    return;
  }

  auto* caps = gst_pad_get_current_caps(new_pad);
  if (caps == nullptr) {
    caps = gst_pad_query_caps(new_pad, nullptr);
  }

  bool is_h264_video = false;
  if (caps != nullptr && !gst_caps_is_empty(caps)) {
    const auto* structure = gst_caps_get_structure(caps, 0);
    const auto* media = gst_structure_get_string(structure, "media");
    const auto* encoding = gst_structure_get_string(structure, "encoding-name");
    is_h264_video = media != nullptr && encoding != nullptr
      && g_ascii_strcasecmp(media, "video") == 0
      && g_ascii_strcasecmp(encoding, "H264") == 0;
  }

  if (is_h264_video && gst_pad_link(new_pad, sink_pad) != GST_PAD_LINK_OK) {
    self->last_result_.store(RCH_RESULT_RTSP_FAILURE, std::memory_order_release);
  }

  if (caps != nullptr) {
    gst_caps_unref(caps);
  }
  gst_object_unref(sink_pad);
}

GstFlowReturn SingleCameraIngest::OnNewSample(GstAppSink* sink, gpointer user_data)
{
  auto* self = static_cast<SingleCameraIngest*>(user_data);
  auto* sample = gst_app_sink_pull_sample(sink);
  if (sample == nullptr) {
    return GST_FLOW_ERROR;
  }

  try {
    self->latest_frame_.Publish(sample);
  } catch (...) {
    // Never unwind through GStreamer's C callback boundary. The resulting bus
    // error is handled by the monitor; state changes cannot run on this thread.
    gst_sample_unref(sample);
    return GST_FLOW_ERROR;
  }
  gst_sample_unref(sample);

  rch_camera_state expected_state = RCH_CAMERA_STATE_STARTING;
  self->state_.compare_exchange_strong(expected_state, RCH_CAMERA_STATE_RECEIVING,
                                      std::memory_order_acq_rel);
  return GST_FLOW_OK;
}

}  // namespace robocamhub::ingest
