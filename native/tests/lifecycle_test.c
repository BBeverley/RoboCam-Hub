#include "robocamhub_native.h"

#include <stddef.h>
#include <stdio.h>

enum { lifecycle_iterations = 10000 };

static int expect(int condition, const char* message)
{
  if (condition) {
    return 1;
  }

  fprintf(stderr, "FAILED: %s\n", message);
  return 0;
}

int main(void)
{
  if (!expect(sizeof(rch_result) == sizeof(int32_t), "result type must be fixed-width")) {
    return 1;
  }

  if (!expect(rch_get_abi_version() == RCH_ABI_VERSION, "ABI version must match the public header")) {
    return 1;
  }

  if (!expect(rch_engine_create(NULL) == RCH_RESULT_INVALID_ARGUMENT,
              "create must reject a null output pointer")) {
    return 1;
  }

  if (!expect(rch_engine_destroy(NULL) == RCH_RESULT_INVALID_HANDLE,
              "destroy must reject a null engine handle")) {
    return 1;
  }

  for (int iteration = 0; iteration < lifecycle_iterations; ++iteration) {
    rch_engine_handle engine = NULL;

    if (!expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine creation must succeed")) {
      return 1;
    }

    if (!expect(engine != NULL, "successful creation must return a handle")) {
      return 1;
    }

    if (!expect(rch_engine_destroy(engine) == RCH_RESULT_OK, "engine destruction must succeed")) {
      return 1;
    }
  }

  return 0;
}
