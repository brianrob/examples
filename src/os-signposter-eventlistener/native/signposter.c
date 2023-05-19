//
//  signposter.c
//  signposterlib
//
//  Created by Brian Robbins on 5/16/23.
//

#include "signposter.h"

void init(void)
{
    log_handle = os_log_create("com.brianrob.signposter", OS_LOG_CATEGORY_POINTS_OF_INTEREST);
    signpost_id = os_signpost_id_generate(log_handle);
}

void emit_signpost(void)
{
    os_signpost_event_emit(log_handle, signpost_id, "My Event 1", "Value: %s", "String");
}
