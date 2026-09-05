#ifndef ROBOCAMHUB_NDI_SENDER_TEST_HOOKS_H
#define ROBOCAMHUB_NDI_SENDER_TEST_HOOKS_H

#include "robocamhub_native.h"

#include <cstdint>

namespace robocamhub::testing {

rch_result SetNdiSenderBackendDelay(
  rch_ndi_sender_handle sender,
  std::uint32_t delay_ms) noexcept;

}  // namespace robocamhub::testing

#endif
