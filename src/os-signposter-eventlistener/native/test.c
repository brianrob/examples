#include <stdio.h>
#include "signposter.h"

int main(int argc, const char * argv[]) {
    printf("Emit sample signpost.\n");
    void * log_handle = create_log_handle("com.test.app");
    unsigned long signpost_id = generate_signpost_id(log_handle);
    emit_signpost_event(log_handle, signpost_id, "test payload");

    unsigned long signpost_id2 = generate_signpost_id(log_handle);
    emit_signpost_start(log_handle, signpost_id2, "TestPayload1");
    sleep(1);
    emit_signpost_stop(log_handle, signpost_id2, "TestPayload2");

    return 0;
}