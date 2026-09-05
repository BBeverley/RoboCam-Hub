#ifndef ROBOCAMHUB_PREVIEW_TEST_HOOKS_H
#define ROBOCAMHUB_PREVIEW_TEST_HOOKS_H

#include <cstdint>

namespace robocamhub::testing {

void SetPreviewPresentationDelayMs(std::uint32_t delay_ms) noexcept;
void RequestPreviewSurfaceRecreation() noexcept;
[[nodiscard]] std::uint32_t ActivePreviewSurfaceCount() noexcept;

}  // namespace robocamhub::testing

#endif
