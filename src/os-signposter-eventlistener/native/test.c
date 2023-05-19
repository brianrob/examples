#include <stdio.h>
#include "signposter.h"

int main(int argc, const char * argv[]) {
    printf("Emit sample signpost.\n");
    void * log_handle = create_log_handle("com.test.app");
    unsigned long signpost_id = generate_signpost_id(log_handle);
    emit_signpost_event(log_handle, signpost_id, "test payload");
    return 0;
}