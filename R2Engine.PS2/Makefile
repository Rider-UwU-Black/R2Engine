EE_BIN = bin/r2engine-ps2-probe.elf
EE_OBJS = src/r2_vu1_transform.o src/main.o src/r2_ps2_platform.o
EE_INCS = -Iinclude -I$(GSKIT)/include
EE_LDFLAGS = -L$(GSKIT)/lib
EE_LIBS = -lgskit -ldmakit -lpacket2 -ldma -lpad -laudsrv -lmc -lm -lc

EE_DVP = dvp-as

ifeq ($(PROGRESSIVE),1)
EE_INCS += -DR2_PROGRESSIVE_SCAN
endif

ifeq ($(VU1),0)
EE_INCS += -DR2_VU1_LIVE_DISPATCH=0
endif

ifeq ($(DEVELOPMENT),1)
EE_INCS += -DR2_DEVELOPMENT_BUILD
endif

ifeq ($(FINAL),1)
EE_BIN = bin/r2engine-ps2-final.elf
EE_OBJS = obj-final/r2_vu1_transform.o obj-final/main.o obj-final/r2_ps2_platform.o
EE_INCS += -DR2_DISC_BUILD
EE_LIBS += -lcdvd
endif

all: $(EE_BIN)

.PHONY: FORCE
FORCE:

$(EE_OBJS): FORCE

src/r2_ps2_platform.o: include/r2_ps2_platform.h include/r2_camera_sweep.h include/r2_animation_playback.h include/r2_animation_controller.h

obj-final/%.o: src/%.c include/r2_ps2_platform.h include/r2_camera_sweep.h include/r2_animation_playback.h include/r2_animation_controller.h
	mkdir -p obj-final
	$(EE_CC) $(EE_CFLAGS) $(EE_INCS) -c $< -o $@

src/%.o: src/%.vsm
	$(EE_DVP) $< -o $@

obj-final/%.o: src/%.vsm
	mkdir -p obj-final
	$(EE_DVP) $< -o $@

clean:
	rm -f -r bin obj src/*.o *.map

run: $(EE_BIN)
	ps2client execee host:$(EE_BIN)

include $(PS2SDK)/samples/Makefile.pref
include $(PS2SDK)/samples/Makefile.eeglobal
