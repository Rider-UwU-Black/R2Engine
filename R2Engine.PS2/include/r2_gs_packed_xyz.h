#ifndef R2_GS_PACKED_XYZ_H
#define R2_GS_PACKED_XYZ_H

#include <stdint.h>

/* GIF PACKED format, not the 64-bit A+D XYZF register representation.
   XYZ2: word 2 is Z32; XYZF2: word 2 bits 4..27 are Z24.
   XYZF2 word 3 bits 4..11 are F8; bit 15 is ADC (must remain clear).
   Reference: PCSX2/pcsx2, pcsx2/GS/GSRegs.h, GIFPackedXYZF2/XYZ2.
   https://github.com/PCSX2/pcsx2/blob/master/pcsx2/GS/GSRegs.h
   Copying an unshifted XYZ2 depth into XYZF2 divides its interpreted value by
   sixteen, so correctly packed terrain can overwrite the nearer character. */
static inline void r2_pack_gs_xyz(uint32_t words[4], uint16_t x, uint16_t y,
    uint32_t z, uint8_t fog, int fog_enabled)
{
    words[0] = x;
    words[1] = y;
    words[2] = fog_enabled ? ((z & 0x00ffffffu) << 4) : z;
    words[3] = fog_enabled ? ((uint32_t)fog << 4) : 0u;
}

#endif
