#ifndef R2_SAVE_IDENTITY_H
#define R2_SAVE_IDENTITY_H
#include <string.h>
#include <stdio.h>
/* R2SI, flags (bit 0: explicit legacy import), zero-terminated 20-char ID. */
static inline int r2_save_identity(const unsigned char *data,unsigned size,char folder[32],int *legacy)
{
    if(!data || size!=32 || memcmp(data,"R2SI",4) || data[4]>1 || data[5] || data[6] || data[7])return 0;
    unsigned length=0;
    while(length<24 && data[8+length]){
        unsigned char c=data[8+length];
        if(length>=20 || !((c>='A' && c<='Z') || (c>='0' && c<='9') || c=='_' || c=='-'))return 0;
        length++;
    }
    if(!length)return 0;
    for(unsigned i=8+length;i<32;i++)if(data[i])return 0;
    snprintf(folder,32,"/R2-%s",(const char *)data+8);*legacy=data[4];return 1;
}
static inline int r2_save_legacy_allowed(int enabled,int primary_error,int recovery_error)
{ return enabled && primary_error==1 && recovery_error==1; }
#endif
