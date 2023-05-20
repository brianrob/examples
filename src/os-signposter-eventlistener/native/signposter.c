#include "signposter.h"

void *create_log_handle(const char *subsystem_name)
{
    return os_log_create(subsystem_name, OS_LOG_CATEGORY_POINTS_OF_INTEREST);
}

unsigned long generate_signpost_id(void *log_handle)
{
    return (unsigned long)os_signpost_id_generate(log_handle);
}

void emit_signpost_event(void *log_handle, unsigned long signpost_id, const char *payload)
{
    os_signpost_event_emit(log_handle, signpost_id, "EventSource", "%{public}s", payload);
}
