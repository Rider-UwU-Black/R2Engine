#include <assert.h>
#include <stdio.h>
#include "../../R2Engine.PS2/include/r2_save_identity.h"
int main(void)
{
    unsigned char data[32]={'R','2','S','I',0,0,0,0,'G','A','M','E','_','A'};
    char folder[32];int legacy=0;
    assert(r2_save_identity(data,32,folder,&legacy));assert(!strcmp(folder,"/R2-GAME_A"));assert(!legacy);
    data[13]='B';assert(r2_save_identity(data,32,folder,&legacy));assert(!strcmp(folder,"/R2-GAME_B"));
    data[4]=1;assert(r2_save_identity(data,32,folder,&legacy));assert(legacy);
    assert(r2_save_legacy_allowed(legacy,1,1));
    assert(!r2_save_legacy_allowed(0,1,1));
    for(int error=2;error<=9;error++){
        assert(!r2_save_legacy_allowed(1,error,1));assert(!r2_save_legacy_allowed(1,1,error));
    }
    data[8]='/';assert(!r2_save_identity(data,32,folder,&legacy));data[8]='G';
    data[31]='X';assert(!r2_save_identity(data,32,folder,&legacy));data[31]=0;
    data[4]=2;assert(!r2_save_identity(data,32,folder,&legacy));data[4]=0;
    assert(!r2_save_identity(data,31,folder,&legacy));
    puts("PASS: separate game directories, explicit migration only for absent slots, malformed identity rejection");
    return 0;
}
