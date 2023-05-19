#include <stdio.h>
#include "signposter.h"

int main(int argc, const char * argv[]) {
    printf("Emit sample signpost.\n");
    init();
    emit_signpost();
    return 0;
}