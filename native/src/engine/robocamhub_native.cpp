#include "robocamhub_native.h"

#include <cstdlib>

struct rch_engine {
  uint32_t abi_version;
};

extern "C" uint32_t rch_get_abi_version(void) noexcept
{
  return RCH_ABI_VERSION;
}

extern "C" rch_result rch_engine_create(rch_engine_handle* out_engine) noexcept
{
  if (out_engine == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  *out_engine = nullptr;

  auto* engine = static_cast<rch_engine*>(std::malloc(sizeof(rch_engine)));
  if (engine == nullptr) {
    return RCH_RESULT_OUT_OF_MEMORY;
  }

  engine->abi_version = RCH_ABI_VERSION;
  *out_engine = engine;
  return RCH_RESULT_OK;
}

extern "C" rch_result rch_engine_destroy(rch_engine_handle engine) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  std::free(engine);
  return RCH_RESULT_OK;
}
