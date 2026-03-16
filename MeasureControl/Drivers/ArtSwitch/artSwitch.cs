using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MeasureControl.Drivers.ArtSwitch
{
    class artSwitch
    {

        /*
            #include <IviVisaType.h>
            #include "artSwitchTopologies.h"
        */
        public const Int32 VI_SUCCESS = (0);

        /*- Other VISA Definitions --------------------------------------------------*/

        public const UInt32 VI_NULL = (0);
        public const UInt32 VI_TRUE = (1);
        public const UInt32 VI_FALSE = (0);
        /****************************************************************************
        *----------------- Instrument Driver Revision Information -----------------*
        ****************************************************************************/

        public const UInt32 ARTSWITCH_MAJOR_VERSION = 1;      /* Instrument driver major version */
        public const UInt32 ARTSWITCH_MINOR_VERSION = 0;      /* Instrument driver minor version */
        public const UInt32 ARTSWITCH_CLASS_SPEC_MAJOR_VERSION = 4;      /* Class specification major version */
        public const UInt32 ARTSWITCH_CLASS_SPEC_MINOR_VERSION = 0;      /* Class specification minor version */
        /*
        public const String ARTSWITCH_SUPPORTED_INSTRUMENT_MODELS = "PXI2601,"\
                                                                "PXI2602,"\
                                                                "PXI2603";
        */
        public const String ARTSWITCH_DRIVER_VENDOR = "ART Technology";
        public const String ARTSWITCH_DRIVER_DESCRIPTION = "ART-SWITCH Driver";

        /****************************************************************************
        *---------------------------- Attribute Defines ---------------------------*
        ****************************************************************************/

        public const Int32 IVI_ATTR_BASE = 1000000;

        public const Int32 IVI_ENGINE_PUBLIC_ATTR_BASE = (IVI_ATTR_BASE + 50000);   /* base for public attributes of the IVI engine */

        public const Int32 IVI_SPECIFIC_PUBLIC_ATTR_BASE = (IVI_ATTR_BASE + 150000);   /* base for public attributes of specific drivers */

        public const Int32 IVI_CLASS_PUBLIC_ATTR_BASE = (IVI_ATTR_BASE + 250000);   /* base for public attributes of class drivers */


        /*- IVI Inherent Instrument Attributes ---------------------------------*/

        /*- User Options -------------------------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_RANGE_CHECK = (IVI_ENGINE_PUBLIC_ATTR_BASE + 2);      /* UInt16 */
        public const Int32 ARTSWITCH_ATTR_QUERY_INSTRUMENT_STATUS = (IVI_ENGINE_PUBLIC_ATTR_BASE + 3);       /* UInt16 */
        public const Int32 ARTSWITCH_ATTR_CACHE = (IVI_ENGINE_PUBLIC_ATTR_BASE + 4);       /* UInt16 */
        public const Int32 ARTSWITCH_ATTR_SIMULATE = (IVI_ENGINE_PUBLIC_ATTR_BASE + 5);       /* UInt16 */
        public const Int32 ARTSWITCH_ATTR_RECORD_COERCIONS = (IVI_ENGINE_PUBLIC_ATTR_BASE + 6);       /* UInt16 */
        public const Int32 ARTSWITCH_ATTR_INTERCHANGE_CHECK = (IVI_ENGINE_PUBLIC_ATTR_BASE + 21);      /* UInt16 */

        /*- Instrument Capabilities --------------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_CHANNEL_COUNT = (IVI_ENGINE_PUBLIC_ATTR_BASE + 203);     /* Int32,  read-only  */
        public const Int32 ARTSWITCH_ATTR_GROUP_CAPABILITIES = (IVI_ENGINE_PUBLIC_ATTR_BASE + 401);     /* ViString, read-only */

        /*- Driver Information  ------------------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_SPECIFIC_DRIVER_PREFIX = (IVI_ENGINE_PUBLIC_ATTR_BASE + 302);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_SUPPORTED_INSTRUMENT_MODELS = (IVI_ENGINE_PUBLIC_ATTR_BASE + 327);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_INSTRUMENT_MANUFACTURER = (IVI_ENGINE_PUBLIC_ATTR_BASE + 511);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_INSTRUMENT_MODEL = (IVI_ENGINE_PUBLIC_ATTR_BASE + 512);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_INSTRUMENT_FIRMWARE_REVISION = (IVI_ENGINE_PUBLIC_ATTR_BASE + 510);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_SPECIFIC_DRIVER_REVISION = (IVI_ENGINE_PUBLIC_ATTR_BASE + 551);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_SPECIFIC_DRIVER_VENDOR = (IVI_ENGINE_PUBLIC_ATTR_BASE + 513);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_SPECIFIC_DRIVER_CLASS_SPEC_MAJOR_VERSION = (IVI_ENGINE_PUBLIC_ATTR_BASE + 515); /* Int32, read-only */
        public const Int32 ARTSWITCH_ATTR_SPECIFIC_DRIVER_CLASS_SPEC_MINOR_VERSION = (IVI_ENGINE_PUBLIC_ATTR_BASE + 516); /* Int32, read-only */
        public const Int32 ARTSWITCH_ATTR_SPECIFIC_DRIVER_DESCRIPTION = (IVI_ENGINE_PUBLIC_ATTR_BASE + 514);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_DRIVER_SETUP = (IVI_ENGINE_PUBLIC_ATTR_BASE + 7);       /* ViString, read-only  */

        /*- Advanced Session Information ---------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_LOGICAL_NAME = (IVI_ENGINE_PUBLIC_ATTR_BASE + 305);     /* ViString, read-only  */
        public const Int32 ARTSWITCH_ATTR_IO_RESOURCE_DESCRIPTOR = (IVI_ENGINE_PUBLIC_ATTR_BASE + 304);     /* ViString, read-only  */

        /*- Configuration Attributes -------------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_IS_SOURCE_CHANNEL = (IVI_CLASS_PUBLIC_ATTR_BASE + 1);       /* UInt16, channel-based */
        public const Int32 ARTSWITCH_ATTR_IS_CONFIGURATION_CHANNEL = (IVI_CLASS_PUBLIC_ATTR_BASE + 3);       /* UInt16, channel-based */

        /*- Status Attributes --------------------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_IS_DEBOUNCED = (IVI_CLASS_PUBLIC_ATTR_BASE + 2);       /* UInt16, read-only */

        /*- Device Information Attributes --------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_SETTLING_TIME = (IVI_CLASS_PUBLIC_ATTR_BASE + 4);       /* Double, channel-based */
        public const Int32 ARTSWITCH_ATTR_BANDWIDTH = (IVI_CLASS_PUBLIC_ATTR_BASE + 5);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_DC_VOLTAGE = (IVI_CLASS_PUBLIC_ATTR_BASE + 6);       /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_AC_VOLTAGE = (IVI_CLASS_PUBLIC_ATTR_BASE + 7);       /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_SWITCHING_DC_CURRENT = (IVI_CLASS_PUBLIC_ATTR_BASE + 8);       /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_SWITCHING_AC_CURRENT = (IVI_CLASS_PUBLIC_ATTR_BASE + 9);       /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_CARRY_DC_CURRENT = (IVI_CLASS_PUBLIC_ATTR_BASE + 10);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_CARRY_AC_CURRENT = (IVI_CLASS_PUBLIC_ATTR_BASE + 11);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_SWITCHING_DC_POWER = (IVI_CLASS_PUBLIC_ATTR_BASE + 12);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_SWITCHING_AC_POWER = (IVI_CLASS_PUBLIC_ATTR_BASE + 13);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_CARRY_DC_POWER = (IVI_CLASS_PUBLIC_ATTR_BASE + 14);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_MAX_CARRY_AC_POWER = (IVI_CLASS_PUBLIC_ATTR_BASE + 15);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_CHARACTERISTIC_IMPEDANCE = (IVI_CLASS_PUBLIC_ATTR_BASE + 16);      /* Double, channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_WIRE_MODE = (IVI_CLASS_PUBLIC_ATTR_BASE + 17);      /* Int32,  channel-based, read-only */
        public const Int32 ARTSWITCH_ATTR_NUM_OF_ROWS = (IVI_CLASS_PUBLIC_ATTR_BASE + 18);      /* Int32,  read-only */
        public const Int32 ARTSWITCH_ATTR_NUM_OF_COLUMNS = (IVI_CLASS_PUBLIC_ATTR_BASE + 19);     /* Int32,  read-only */

        /*- Scanning Attributes ------------------------------------------------*/
        public const Int32 ARTSWITCH_ATTR_SCAN_LIST = (IVI_CLASS_PUBLIC_ATTR_BASE + 20);      /* ViString */
        public const Int32 ARTSWITCH_ATTR_SCAN_MODE = (IVI_CLASS_PUBLIC_ATTR_BASE + 21);      /* Int32  */
        public const Int32 ARTSWITCH_ATTR_TRIGGER_INPUT = (IVI_CLASS_PUBLIC_ATTR_BASE + 22);      /* Int32  */
        public const Int32 ARTSWITCH_ATTR_SCAN_ADVANCED_OUTPUT = (IVI_CLASS_PUBLIC_ATTR_BASE + 23);      /* Int32  */
        public const Int32 ARTSWITCH_ATTR_IS_SCANNING = (IVI_CLASS_PUBLIC_ATTR_BASE + 24);      /* UInt16, read-only */
        public const Int32 ARTSWITCH_ATTR_SCAN_DELAY = (IVI_CLASS_PUBLIC_ATTR_BASE + 25);      /* Double */
        public const Int32 ARTSWITCH_ATTR_CONTINUOUS_SCAN = (IVI_CLASS_PUBLIC_ATTR_BASE + 26);      /* UInt16 */

        /*- artSwitch specific driver attributes --------------------------------*/
        public const Int32 ARTSWITCH_ATTR_IS_WAITING_FOR_TRIG = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 4);    /* UInt16, read-only */
        public const Int32 ARTSWITCH_ATTR_TRIGGER_INPUT_POLARITY = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 10);   /* Int32  */
        public const Int32 ARTSWITCH_ATTR_SCAN_ADVANCED_POLARITY = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 11);   /* Int32  */
        //public const UInt32 ARTSWITCH_ATTR_PARSED_SCAN_LIST                 (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 12L)   /* ViString, read-only */
        public const Int32 ARTSWITCH_ATTR_HANDSHAKING_INITIATION = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 13);   /* Int32  */
        public const Int32 ARTSWITCH_ATTR_NUMBER_OF_RELAYS = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 14);   /* Int32, read-only */
        public const Int32 ARTSWITCH_ATTR_SERIAL_NUMBER = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 15);   /* ViString, read-only */
        public const Int32 ARTSWITCH_ATTR_DIGITAL_FILTER_ENABLE = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 16);   /* UInt16 */
        public const Int32 ARTSWITCH_ATTR_POWER_DOWN_LATCHING_RELAYS_AFTER_DEBOUNCE = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 17);   /* UInt16 */

        // Not impletement
        public const Int32 ARTSWITCH_ATTR_ANALOG_BUS_SHARING_ENABLE = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 18);   /* UInt16, channel-based */
        public const Int32 ARTSWITCH_ATTR_TEMPERATURE = (IVI_SPECIFIC_PUBLIC_ATTR_BASE + 19);   /* Double  */


        /****************************************************************************
        *------------------------ Attribute Value Defines -------------------------*
        ****************************************************************************/
        /* Defined values for ARTSWITCH_ATTR_SCAN_MODE */
        public const Int32 ARTSWITCH_VAL_NONE = (0);
        public const Int32 ARTSWITCH_VAL_BREAK_BEFORE_MAKE = (1);
        public const Int32 ARTSWITCH_VAL_BREAK_AFTER_MAKE = (2);

        /* Defined values for ARTSWITCH_ATTR_TRIGGER_INPUT */

        public const Int32 IVISWTCH_VAL_TRIGGER_INPUT_SPECIFIC_EXT_BASE = (1000);


        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE = (IVISWTCH_VAL_TRIGGER_INPUT_SPECIFIC_EXT_BASE + 20);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE = (IVISWTCH_VAL_TRIGGER_INPUT_SPECIFIC_EXT_BASE + 40);

        public const Int32 ARTSWITCH_VAL_IMMEDIATE = (1);
        public const Int32 ARTSWITCH_VAL_EXTERNAL = (2);
        public const Int32 ARTSWITCH_VAL_SOFTWARE_TRIG = (3);
        public const Int32 ARTSWITCH_VAL_TTL0 = (111);
        public const Int32 ARTSWITCH_VAL_TTL1 = (112);
        public const Int32 ARTSWITCH_VAL_TTL2 = (113);
        public const Int32 ARTSWITCH_VAL_TTL3 = (114);
        public const Int32 ARTSWITCH_VAL_TTL4 = (115);
        public const Int32 ARTSWITCH_VAL_TTL5 = (116);
        public const Int32 ARTSWITCH_VAL_TTL6 = (117);
        public const Int32 ARTSWITCH_VAL_TTL7 = (118);
        public const Int32 ARTSWITCH_VAL_PXI_STAR = (125);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR = (IVISWTCH_VAL_TRIGGER_INPUT_SPECIFIC_EXT_BASE + 0);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR = (IVISWTCH_VAL_TRIGGER_INPUT_SPECIFIC_EXT_BASE + 1);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE1 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 1);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE2 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 2);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE3 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 3);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE4 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 4);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE5 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 5);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE6 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 6);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE7 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 7);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE8 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 8);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE9 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 9);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE10 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 10);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE11 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 11);
        public const Int32 ARTSWITCH_VAL_REARCONNECTOR_MODULE12 = (ARTSWITCH_VAL_REARCONNECTOR_MODULE_BASE + 12);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE1 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 1);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE2 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 2);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE3 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 3);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE4 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 4);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE5 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 5);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE6 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 6);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE7 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 7);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE8 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 8);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE9 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 9);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE10 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 10);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE11 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 11);
        public const Int32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE12 = (ARTSWITCH_VAL_FRONTCONNECTOR_MODULE_BASE + 12);


        /* Defined values for ARTSWITCH_ATTR_SCAN_ADVANCED_OUTPUT; */
        /* public const UInt32 ARTSWITCH_VAL_NONE = DEFINED ABOVE ;*/
        /* public const UInt32 ARTSWITCH_VAL_EXTERNAL = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL0 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL1 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL2 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL3 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL4 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL5 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL6 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_TTL7 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_PXI_STAR = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE1 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE2 = DEFINED ABOVE ;*/
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE3 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE4 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE5 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE6 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE7 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE8 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE9 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE10 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE11 = DEFINED ABOVE; */
        /* public const UInt32 ARTSWITCH_VAL_FRONTCONNECTOR_MODULE12 = DEFINED ABOVE; */

        /* Defined values for ARTSWITCH_ATTR_WIRE_MODE */
        public const Int32 ARTSWITCH_VAL_1_WIRE = 1;
        public const Int32 ARTSWITCH_VAL_2_WIRE = 2;
        public const Int32 ARTSWITCH_VAL_4_WIRE = 4;

        /* Defined values for artSwitch_CanConnect path capability parameter */
        public const Int32 ARTSWITCH_VAL_PATH_AVAILABLE = (1);
        public const Int32 ARTSWITCH_VAL_PATH_EXISTS = (2);
        public const Int32 ARTSWITCH_VAL_PATH_UNSUPPORTED = (3);
        public const Int32 ARTSWITCH_VAL_RSRC_IN_USE = (4);
        public const Int32 ARTSWITCH_VAL_SOURCE_CONFLICT = (5);
        public const Int32 ARTSWITCH_VAL_CHANNEL_NOT_AVAILABLE = (6);

        /* Defined values for ARTSWITCH_ATTR_SCAN_ADVANCED_POLARITY and ARTSWITCH_TRIGGER_INPUT_POLARITY */
        public const Int32 ARTSWITCH_VAL_RISING_EDGE = 0;
        public const Int32 ARTSWITCH_VAL_FALLING_EDGE = 1;

        /* Defined values for the Scan function */
        public const Int32 ARTSWITCH_VAL_MEASUREMENT_DEVICE_INITIATED = 0;
        public const Int32 ARTSWITCH_VAL_DMM_INITIATED = ARTSWITCH_VAL_MEASUREMENT_DEVICE_INITIATED;
        public const Int32 ARTSWITCH_VAL_SWITCH_INITIATED = 1;

        /* Defined values for the artSwitch_GetRelayPosition's position parameter */
        public const Int32 ARTSWITCH_VAL_OPEN = 10;
        public const Int32 ARTSWITCH_VAL_CLOSED = 11;

        /* Defined values for the artSwitch_RelayControl function */
        public const Int32 ARTSWITCH_VAL_OPEN_RELAY = 20;
        public const Int32 ARTSWITCH_VAL_CLOSE_RELAY = 21;

        /****************************************************************************
        *---------------- Instrument Driver Function Declarations -----------------*
        ****************************************************************************/

        /*- Session Management Functions ---------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_init(string resourceName, UInt16 idQuery, UInt16 resetDevice, ref UInt32 newVi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_InitWithOptions(string resourceName, UInt16 idQuery, UInt16 resetDevice, string optionString, ref UInt32 newVi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_InitWithTopology(string resourceName, IntPtr topology, UInt16 simulate, UInt16 resetDevice, ref UInt32 newVi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_close(UInt32 vi);

        /*- Locking Functions --------------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_LockSession(UInt32 vi, ref UInt16 callerHasLock);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_UnlockSession(UInt32 vi, ref UInt16 callerHasLock);

        /*- Switch Routing Functions -------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_Connect(UInt32 vi, string channel1, string channel2);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ConnectMultiple(UInt32 vi, ref Byte connectionList);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_Disconnect(UInt32 vi, string channel1, string channel2);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_DisconnectMultiple(UInt32 vi, ref Byte disconnectionList);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_DisconnectAll(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetPath(UInt32 vi, string channel1, string channel2, Int32 bufferSize, Byte[] pathList);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetPath(UInt32 vi, ref Byte pathList);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_CanConnect(UInt32 vi, string channel1, string channel2, IntPtr pathCapability);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_IsDebounced(UInt32 vi, ref UInt16 isDebounced);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_WaitForDebounce(UInt32 vi, Int32 maxTime);

        /*- Scanning Functions -------------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_Scan(UInt32 vi, string scanList, Int16 initiation);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_InitiateScan(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_AbortScan(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_IsScanning(UInt32 vi, ref UInt16 isScanning);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_WaitForScanComplete(UInt32 vi, Int32 maxTime);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SendSoftwareTrigger(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ConfigureScanList(UInt32 vi, string scanList, Int32 scanMode);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ConfigureScanTrigger(UInt32 vi, Double scanDelay, Int32 triggerInput, Int32 scanAdvancedOutput);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetContinuousScan(UInt32 vi, UInt32 continuousScan);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_RouteTriggerInput(UInt32 vi, Int32 triggerInputConnector, Int32 triggerInputBusLine, UInt16 invert);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_RouteScanAdvancedOutput(UInt32 vi, Int32 scanAdvancedOutputConnector, Int32 scanAdvancedOutputBusLine, UInt16 invert);


        /*- Error Functions ----------------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_error_query(UInt32 vi, ref Int32 errorCode, Byte[] errorMessage);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetError(UInt32 vi, ref Int32 errorCode, Int32 bufferSize, Byte[] description);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ClearError(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_error_message(UInt32 vi, Int32 errorCode, Byte[] errorMessage);

        /*- Channel Info Functions ---------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetChannelName(UInt32 vi, Int32 index, Int32 bufferSize, Byte[] name);

        /*- Relay Operation Functions -------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetRelayName(UInt32 vi, Int32 index, Int32 bufferSize, Byte[] name);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetRelayCount(UInt32 vi, string relayName, ref Int32 count);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetRelayPosition(UInt32 vi, string relayName, ref Int32 position);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_RelayControl(UInt32 vi, string relayNames, Int32 relayAction);

        /*- Interchangeability Checking Functions ------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetNextInterchangeWarning(UInt32 vi, Int32 bufferSize, Byte[] warnString);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ResetInterchangeCheck(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ClearInterchangeWarnings(UInt32 vi);

        /*- Coercion Functions -------------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetNextCoercionRecord(UInt32 vi, Int32 bufferSize, Byte[] record);

        /*- Utility Functions --------------------------------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_Commit(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_InvalidateAllAttributes(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_ResetWithDefaults(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_Disable(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_reset(UInt32 vi);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_self_test(UInt32 vi, ref Int16 selfTestResult, Byte[] selfTestMessage);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_revision_query(UInt32 vi, Byte[] instrumentDriverRevision, Byte[] firmwareRevision);

        /*- Set, Get, and Check Attribute Functions ----------------------------*/
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetAttributeInt32(UInt32 vi, string channelName, UInt32 attribute, ref Int32 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetAttributeDouble(UInt32 vi, string channelName, UInt32 attribute, ref Double value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetAttributeViString(UInt32 vi, string channelName, UInt32 attribute, Int32 bufferSize, Byte[] value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetAttributeUInt32(UInt32 vi, string channelName, UInt32 attribute, ref UInt32 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_GetAttributeUInt16(UInt32 vi, string channelName, UInt32 attribute, ref UInt16 value);

        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetAttributeInt32(UInt32 vi, string channelName, UInt32 attribute, Int32 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetAttributeDouble(UInt32 vi, string channelName, UInt32 attribute, Double value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetAttributeViString(UInt32 vi, string channelName, UInt32 attribute, ref Byte value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetAttributeUInt32(UInt32 vi, string channelName, UInt32 attribute, UInt32 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_SetAttributeUInt16(UInt32 vi, string channelName, UInt32 attribute, UInt16 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_CheckAttributeInt32(UInt32 vi, string channelName, UInt32 attribute, Int32 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_CheckAttributeDouble(UInt32 vi, string channelName, UInt32 attribute, Double value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_CheckAttributeViString(UInt32 vi, string channelName, UInt32 attribute, ref Byte value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_CheckAttributeUInt32(UInt32 vi, string channelName, UInt32 attribute, UInt32 value);
        [DllImport("artSwitch_64.DLL", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern Int32 artSwitch_CheckAttributeUInt16(UInt32 vi, string channelName, UInt32 attribute, UInt16 value);

        /****************************************************************************
        *------------------------ Error And Completion Codes ----------------------*
        ****************************************************************************/
        public const Int32 IVI_STATUS_CODE_BASE = 0x3FFA0000;

        public const Int32 IVI_WARN_BASE = (IVI_STATUS_CODE_BASE);
        public const Int32 IVI_CROSS_CLASS_WARN_BASE = (IVI_WARN_BASE + 0x1000);
        public const Int32 IVI_CLASS_WARN_BASE = (IVI_WARN_BASE + 0x2000);
        public const Int32 IVI_SPECIFIC_WARN_BASE = (IVI_WARN_BASE + 0x4000);
        public const Int32 _VI_ERROR = (-2147483647 - 1);  /* 0x80000000 */
        public const Int32 IVI_ERROR_BASE = (_VI_ERROR + IVI_STATUS_CODE_BASE);
        public const Int32 IVI_CROSS_CLASS_ERROR_BASE = (IVI_ERROR_BASE + 0x1000);
        public const Int32 IVI_CLASS_ERROR_BASE = (IVI_ERROR_BASE + 0x2000);
        public const Int32 IVI_SPECIFIC_ERROR_BASE = (IVI_ERROR_BASE + 0x4000);

        public const Int32 ARTSWITCH_ERROR_SESSION_ALREADY_OPEN = (IVI_SPECIFIC_ERROR_BASE + 1);
        public const Int32 ARTSWITCH_ERROR_INVALID_RESOURCE_DESCRIPTOR = (IVI_SPECIFIC_ERROR_BASE + 2);
        public const Int32 ARTSWITCH_ERROR_SCANNING_NOT_SUPPORTED = (IVI_SPECIFIC_ERROR_BASE + 3);
        public const Int32 ARTSWITCH_ERROR_MUST_SPECIFY_MODULE = (IVI_SPECIFIC_ERROR_BASE + 4);
        public const Int32 ARTSWITCH_ERROR_MODULE_FIFO_LENGTH_EXCEEDED = (IVI_SPECIFIC_ERROR_BASE + 5);
        public const Int32 ARTSWITCH_ERROR_HW_COMMUNICATE_TMO = (IVI_SPECIFIC_ERROR_BASE + 6);
        public const Int32 ARTSWITCH_ERROR_TTL_BUS_REQUIRED = (IVI_SPECIFIC_ERROR_BASE + 7);
        public const Int32 ARTSWITCH_ERROR_MODULE_IS_BBM_ONLY = (IVI_SPECIFIC_ERROR_BASE + 8);
        public const Int32 ARTSWITCH_ERROR_1127_TTL1_CONFLICT = (IVI_SPECIFIC_ERROR_BASE + 9);
        public const Int32 ARTSWITCH_ERROR_INVALID_DRIVER_SETUP_STRING = (IVI_SPECIFIC_ERROR_BASE + 11);
        public const Int32 ARTSWITCH_ERROR_TOPOLOGY_NOT_SUPPORTED = (IVI_SPECIFIC_ERROR_BASE + 12);
        public const Int32 ARTSWITCH_ERROR_INVALID_TOPOLOGY = (IVI_SPECIFIC_ERROR_BASE + 13);
        public const Int32 ARTSWITCH_ERROR_HARDWARE_UNEXPECTEDLY_RESET = (IVI_SPECIFIC_ERROR_BASE + 14);
        public const Int32 ARTSWITCH_ERROR_HANDSHAKING_INITIATION_CONFLICT = (IVI_SPECIFIC_ERROR_BASE + 15);
        public const Int32 ARTSWITCH_ERROR_LEGACY_DESCRIPTOR_DAQMX_RSC_TYPE = (IVI_SPECIFIC_ERROR_BASE + 16);
        public const Int32 ARTSWITCH_ERROR_DAQ_DESCRIPTOR_LEGACY_RSC_TYPE = (IVI_SPECIFIC_ERROR_BASE + 17);
        public const Int32 ARTSWITCH_ERROR_AMBIGUOUS_MODEL_CODE = (IVI_SPECIFIC_ERROR_BASE + 18);
        public const Int32 ARTSWITCH_ERROR_TRIGGER_INPUT_NOT_SUPPORTED = (IVI_SPECIFIC_ERROR_BASE + 19);
        public const Int32 ARTSWITCH_ERROR_INVALID_TERMINALBLOCK_FOR_TOPOLOGY = (IVI_SPECIFIC_ERROR_BASE + 20);
        public const Int32 ARTSWITCH_ERROR_CANT_INVERT_WHEN_SOURCE_EQUALS_DEST = (IVI_SPECIFIC_ERROR_BASE + 21);
        public const Int32 ARTSWITCH_ERROR_CONFLICTING_TRIGGER_ROUTE_EXISTS = (IVI_SPECIFIC_ERROR_BASE + 22);
        public const Int32 ARTSWITCH_ERROR_INVALID_VALUE_FOR_DEVICE = (IVI_SPECIFIC_ERROR_BASE + 23);
        public const Int32 ARTSWITCH_ERROR_TRIGGER_POLARITY_CONFLICT = (IVI_SPECIFIC_ERROR_BASE + 24);
        public const Int32 ARTSWITCH_ERROR_INTERNAL_ERROR = (IVI_SPECIFIC_ERROR_BASE + 25);
        public const Int32 ARTSWITCH_ERROR_RESET_NEEDED_TO_CHANGE_TOPOLOGY = (IVI_SPECIFIC_ERROR_BASE + 26);
        public const Int32 ARTSWITCH_ERROR_RESERVATION_ERROR = (IVI_SPECIFIC_ERROR_BASE + 27);
        public const Int32 ARTSWITCH_ERROR_ANALOG_BUS_INVALID = (IVI_SPECIFIC_ERROR_BASE + 28);
        public const Int32 ARTSWITCH_ERROR_POWER_LIMIT_EXCEEDED = (IVI_SPECIFIC_ERROR_BASE + 29);
        public const Int32 ARTSWITCH_ERROR_DEVICE_SELF_TEST_FAILED = (IVI_SPECIFIC_ERROR_BASE + 30);
        public const Int32 ARTSWITCH_ERROR_CARD_DETECTED_DOES_NOT_MATCH_EXPECTED_CARD = (IVI_SPECIFIC_ERROR_BASE + 31);
        public const Int32 ARTSWITCH_ERROR_ANALOG_BUS_STATE_INCONSISTENT = (IVI_SPECIFIC_ERROR_BASE + 32);
        public const Int32 ARTSWITCH_ERROR_FIVE_VOLT_DETECT_FAILED = (IVI_SPECIFIC_ERROR_BASE + 33);
        public const Int32 ARTSWITCH_ERROR_SLOT_POWER_LIMIT_EXCEEDED = (IVI_SPECIFIC_ERROR_BASE + 34);
        public const Int32 ARTSWITCH_ERROR_CANNOT_EXCEED_RELAY_DRIVE_LIMIT = (IVI_SPECIFIC_ERROR_BASE + 35);
        public const Int32 ARTSWITCH_ERROR_INVALID_CONNECTION_LIST = (IVI_SPECIFIC_ERROR_BASE + 36);
        public const Int32 ARTSWITCH_ERROR_DISCONNECTION_PATH_NOT_SAME_AS_EXISTING_PATH = (IVI_SPECIFIC_ERROR_BASE + 37);
        public const Int32 ARTSWITCH_ERROR_INVALID_RELAY_NAME = (IVI_SPECIFIC_ERROR_BASE + 38);
        public const Int32 ARTSWITCH_ERROR_ANALOG_BUS_SHARING_DIFFERENT_WIRE_MODES = (IVI_SPECIFIC_ERROR_BASE + 39);
        public const Int32 ARTSWITCH_ERROR_DEVICE_NO_LONGER_SUPPORTED = (IVI_SPECIFIC_ERROR_BASE + 40);
        public const Int32 ARTSWITCH_ERROR_HW_COMMUNICATE_FAILED = (IVI_SPECIFIC_ERROR_BASE + 60);

        public const Int32 ARTSWITCH_WARN_PATH_REMAINS = (IVI_CLASS_WARN_BASE + 1);
        public const Int32 ARTSWITCH_WARN_IMPLICIT_CONNECTION_EXISTS = (IVI_CLASS_WARN_BASE + 2);

        public const Int32 ARTSWITCH_ERROR_INVALID_SWITCH_PATH = (IVI_CLASS_ERROR_BASE + 1);
        public const Int32 ARTSWITCH_ERROR_INVALID_SCAN_LIST = (IVI_CLASS_ERROR_BASE + 2);
        public const Int32 ARTSWITCH_ERROR_RSRC_IN_USE = (IVI_CLASS_ERROR_BASE + 3);
        public const Int32 ARTSWITCH_ERROR_EMPTY_SCAN_LIST = (IVI_CLASS_ERROR_BASE + 4);
        public const Int32 ARTSWITCH_ERROR_EMPTY_SWITCH_PATH = (IVI_CLASS_ERROR_BASE + 5);
        public const Int32 ARTSWITCH_ERROR_SCAN_IN_PROGRESS = (IVI_CLASS_ERROR_BASE + 6);
        public const Int32 ARTSWITCH_ERROR_NO_SCAN_IN_PROGRESS = (IVI_CLASS_ERROR_BASE + 7);
        public const Int32 ARTSWITCH_ERROR_NO_SUCH_PATH = (IVI_CLASS_ERROR_BASE + 8);
        public const Int32 ARTSWITCH_ERROR_IS_CONFIGURATION_CHANNEL = (IVI_CLASS_ERROR_BASE + 9);
        public const Int32 ARTSWITCH_ERROR_NOT_A_CONFIGURATION_CHANNEL = (IVI_CLASS_ERROR_BASE + 10);
        public const Int32 ARTSWITCH_ERROR_ATTEMPT_TO_CONNECT_SOURCES = (IVI_CLASS_ERROR_BASE + 11);
        public const Int32 ARTSWITCH_ERROR_EXPLICIT_CONNECTION_EXISTS = (IVI_CLASS_ERROR_BASE + 12);
        public const Int32 ARTSWITCH_ERROR_LEG_MISSING_FIRST_CHANNEL = (IVI_CLASS_ERROR_BASE + 13);
        public const Int32 ARTSWITCH_ERROR_LEG_MISSING_SECOND_CHANNEL = (IVI_CLASS_ERROR_BASE + 14);
        public const Int32 ARTSWITCH_ERROR_CHANNEL_DUPLICATED_IN_LEG = (IVI_CLASS_ERROR_BASE + 15);
        public const Int32 ARTSWITCH_ERROR_CHANNEL_DUPLICATED_IN_PATH = (IVI_CLASS_ERROR_BASE + 16);
        public const Int32 ARTSWITCH_ERROR_PATH_NOT_FOUND = (IVI_CLASS_ERROR_BASE + 17);
        public const Int32 ARTSWITCH_ERROR_DISCONTINUOUS_PATH = (IVI_CLASS_ERROR_BASE + 18);
        public const Int32 ARTSWITCH_ERROR_CANNOT_CONNECT_DIRECTLY = (IVI_CLASS_ERROR_BASE + 19);
        public const Int32 ARTSWITCH_ERROR_CHANNELS_ALREADY_CONNECTED = (IVI_CLASS_ERROR_BASE + 20);
        public const Int32 ARTSWITCH_ERROR_CANNOT_CONNECT_TO_ITSELF = (IVI_CLASS_ERROR_BASE + 21);
        public const Int32 ARTSWITCH_ERROR_MAX_TIME_EXCEEDED = (IVI_CLASS_ERROR_BASE + 22);


        public const Int32 ARTSWITCH_ERROR_TRIGGER_NOT_SOFTWARE = (IVI_CROSS_CLASS_ERROR_BASE + 1);


        /****************************************************************************
        *---------------------------- End Include File ----------------------------*
        ****************************************************************************/




    }
}
