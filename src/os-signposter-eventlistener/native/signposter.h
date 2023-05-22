#ifndef signposter_h
#define signposter_h

#include <os/signpost.h>

os_log_t log_handle;
os_signpost_id_t signpost_id;

void *create_log_handle(const char *subsystem_name);

unsigned long generate_signpost_id(void *log_handle);

void emit_signpost_event(void *log_handle, unsigned long signpost_id, const char *payload);

void emit_signpost_start(void *log_handle, unsigned long signpost_id, const char *payload);

void emit_signpost_stop(void *log_handle, unsigned long signpost_id, const char *payload);

#endif /* signposter_h */
