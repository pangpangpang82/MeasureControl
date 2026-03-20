#include "extcode.h"
#ifdef __cplusplus
extern "C" {
#endif
typedef struct {
	LStrHandle Model;
	LStrHandle ID;
	LStrHandle PXISlot;
} Cluster;

/*!
 * C:\Program Files (x86)\National Instruments\LabVIEW 
 * 2020\examples\MangoTree\RIO\MT-LVDS\FPGA 
 * Bitfiles\mt-lvdsexample_FPGATarget_loopbackfpga_bM9LRaHO12Q.lvbitx
 */
uint8_t __cdecl MTLVDSLoopback_host(LVBoolean ConfigOSC, 
	double ClockFrequencyHz, LVBoolean StaticTCountUpF, 
	uint16_t LVDS_Data_Sample_Wr, uint16_t PatternMatch, uint16_t NumSamples, 
	Cluster *DevCondition, int32_t *indexOfElement, 
	uint8_t *TriggerSampleLocation, uint16_t arrayWSubsetDeleted[], int32_t len);

MgErr __cdecl LVDLLStatus(char *errStr, int errStrLen, void *module);

void __cdecl SetExecuteVIsInPrivateExecutionSystem(Bool32 value);

#ifdef __cplusplus
} // extern "C"
#endif

