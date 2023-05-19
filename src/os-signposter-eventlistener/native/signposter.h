//
//  signposter.h
//  signposterlib
//
//  Created by Brian Robbins on 5/16/23.
//

#ifndef signposter_h
#define signposter_h

#include <os/signpost.h>

os_log_t log_handle;
os_signpost_id_t signpost_id;

void init(void);

void emit_signpost(void);

#endif /* signposter_h */
