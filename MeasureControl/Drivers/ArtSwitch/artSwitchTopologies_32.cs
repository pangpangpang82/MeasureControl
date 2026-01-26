/****************************************************************************
 *                       ART-SWITCH Topologies
 *---------------------------------------------------------------------------
 *   Copyright (c) ART Technology 1999-2021.  All Rights Reserved.
 *---------------------------------------------------------------------------
 *
 * Title:    artSwitchTopologies.h
 * Purpose:  Define topologies for use with ART-SWITCH
 *
 ****************************************************************************/
using System;
using System.Collections.Generic;
using System.Text;

namespace MeasureControl.Drivers.ArtSwitch
{
    class artSwitchTopologies
    {
        public const String ARTSWITCH_TOPOLOGY_CONFIGURED_TOPOLOGY = "Configured Topology";

        public const String ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX = "2601/2-Wire 4x32 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX = "2601/2-Wire 8x16 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2601_2_WIRE_DUAL_4X16_MATRIX = "2601/2-Wire Dual 4x16 Matrix";

        public const String ARTSWITCH_TOPOLOGY_2602_1_WIRE_64X1_MUX = "2602/1-Wire 64x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2602_1_WIRE_DUAL_32X1_MUX = "2602/1-Wire Dual 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2602_2_WIRE_32X1_MUX = "2602/2-Wire 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2602_2_WIRE_DUAL_16X1_MUX = "2602/2-Wire Dual 16x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2602_4_WIRE_16X1_MUX = "2602/4-Wire 16x1 Mux";

        public const String ARTSWITCH_TOPOLOGY_2603_64_SPST = "2603/64-SPST";
        public const String ARTSWITCH_TOPOLOGY_2603_32_DPST = "2603/32-DPST";

        public const String ARTSWITCH_TOPOLOGY_2604_40_SPDT = "2604/40-SPDT";

        public const String ARTSWITCH_TOPOLOGY_2605_26_DPDT = "2605/26-DPDT";

        public const String ARTSWITCH_TOPOLOGY_2606_16_SPST = "2606/16-SPST";
        public const String ARTSWITCH_TOPOLOGY_2606_8_DPST = "2606/8-DPST";

        public const String ARTSWITCH_TOPOLOGY_2607_40_DPST = "2607/40-DPST";

        public const String ARTSWITCH_TOPOLOGY_2608_10_SPST = "2608/10-SPST";
        public const String ARTSWITCH_TOPOLOGY_2608_5_DPST = "2608/5-DPST";

        public const String ARTSWITCH_TOPOLOGY_2611_2_WIRE_4X16_MATRIX = "2611/2-Wire 4x16 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2611_2_WIRE_8X8_MATRIX = "2611/2-Wire 8x8 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2611_2_WIRE_DUAL_4X8_MATRIX = "2611/2-Wire Dual 4x8 Matrix";

        public const String ARTSWITCH_TOPOLOGY_2612_1_WIRE_64X1_MUX = "2612/1-Wire 64x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2612_1_WIRE_DUAL_32X1_MUX = "2612/1-Wire Dual 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2612_2_WIRE_32X1_MUX = "2612/2-Wire 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2612_2_WIRE_DUAL_16X1_MUX = "2612/2-Wire Dual 16x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2612_4_WIRE_16X1_MUX = "2612/4-Wire 16x1 Mux";

        public const String ARTSWITCH_TOPOLOGY_2613_32_SPST = "2613/32-SPST";
        public const String ARTSWITCH_TOPOLOGY_2613_16_DPST = "2613/16-DPST";
        //public const String ARTSWITCH_TOPOLOGY_2613_16_SPST                    =   "2613/16-SPST";

        public const String ARTSWITCH_TOPOLOGY_2614_16_SPDT = "2614/16-SPDT";
        public const String ARTSWITCH_TOPOLOGY_2614_8_DPDT = "2614/8-DPDT";

        //public const String ARTSWITCH_TOPOLOGY_2616_1_WIRE_4X10_MATRIX         =   "2616/1-Wire 4x10 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2616_1_WIRE_8X10_MATRIX = "2616/1-Wire 8x10 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2616_1_WIRE_4X20_MATRIX = "2616/1-Wire 4x20 Matrix";
        public const String ARTSWITCH_TOPOLOGY_2616_1_WIRE_DUAL_4X10_MATRIX = "2616/1-Wire Dual 4x10 Matrix";

        public const String ARTSWITCH_TOPOLOGY_2620_1_WIRE_120X1_MUX = "2620/1-Wire 120x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2620_2_WIRE_60X1_MUX = "2620/2-Wire 60x1 Mux";

        public const String ARTSWITCH_TOPOLOGY_2621_1_WIRE_128X1_MUX = "2621/1-Wire 128x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_1_WIRE_DUAL_64X1_MUX = "2621/1-Wire Dual 64x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_1_WIRE_QUAD_32X1_MUX = "2621/1-Wire Quad 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_1_WIRE_OCTAL_16X1_MUX = "2621/1-Wire Octal 16x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_2_WIRE_64X1_MUX = "2621/2-Wire 64x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_2_WIRE_DUAL_32X1_MUX = "2621/2-Wire Dual 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_2_WIRE_QUAD_16X1_MUX = "2621/2-Wire Quad 16x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_4_WIRE_32X1_MUX = "2621/4-Wire 32x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2621_4_WIRE_DUAL_16X1_MUX = "2621/4-Wire Dual 16x1 Mux";

        public const String ARTSWITCH_TOPOLOGY_2622_100_SPST = "2622/100-SPST";
        public const String ARTSWITCH_TOPOLOGY_2622_50_DPST = "2622/50-DPST";

        public const String ARTSWITCH_TOPOLOGY_2624_66_SPDT = "2624/66-SPDT";

        public const String ARTSWITCH_TOPOLOGY_2625_1_WIRE_196X1_MUX = "2625/1-Wire 196x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2625_2_WIRE_95X1_MUX = "2625/2-Wire 95x1 Mux";
        public const String ARTSWITCH_TOPOLOGY_2625_2_WIRE_98X1_MUX = "2625/2-Wire 98x1 Mux";

    }
}
