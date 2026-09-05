#ifndef ROBOCAMHUB_NDI_SENDER_BACKEND_H
#define ROBOCAMHUB_NDI_SENDER_BACKEND_H

#include "frames/latest_frame.h"
#include "robocamhub_native.h"

#include <cstdint>

namespace robocamhub::ndi {

struct SenderBackendSendResult final {
  bool accepted{false};
  std::uint32_t result{RCH_RESULT_INTERNAL_ERROR};
  bool receiver_count_known{false};
  std::uint32_t receiver_count{0};
};

[[nodiscard]] void* CreateOfficialSenderBackend(const char* sender_name_utf8) noexcept;
void DestroyOfficialSenderBackend(void* context) noexcept;
[[nodiscard]] SenderBackendSendResult SendOfficialFrame(
  void* context,
  const frames::LatestFrameLease& lease) noexcept;

}  // namespace robocamhub::ndi

#endif
