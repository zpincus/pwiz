﻿/*
 * Original author: Nick Shulman <nicksh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2013 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using pwiz.Common.Chemistry;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.Results;
using pwiz.Skyline.Util;
using pwiz.Skyline.Util.Extensions;
// ReSharper disable VirtualMemberCallInConstructor

namespace pwiz.Skyline.Model.Lib.ChromLib.Data
{
    public class Precursor : ChromLibEntity<Precursor>
    {
        public Precursor()
        {
            Transitions = new List<Transition>();
        }
        public virtual Peptide Peptide { get; set; }
        public virtual int PeptideId { get; set; }
        public virtual SampleFile SampleFile { get; set; }
        public virtual string IsotopeLabel { get; set; }
        public virtual double Mz { get; set; }
        public virtual int Charge { get; set; }
        public virtual int SampleFileId { get; set; }
        public virtual Adduct GetAdduct() { return Adduct.FromChargeProtonated(Charge); }
        public virtual double NeutralMass { get; set; }
        public virtual string ModifiedSequence { get; set; }  // CONSIDER: bspratt/nicksh More appropriately called TextId?
        public virtual double CollisionEnergy { get; set; }
        public virtual double DeclusteringPotential { get; set; }
        public virtual double TotalArea { get; set; }
        public virtual int NumTransitions { get; set; }
        public virtual int NumPoints { get; set; }
        public virtual double AverageMassErrorPPM { get; set; }
        public virtual byte[] Chromatogram { get; set; }

        protected virtual int GetChromatogramFormat()
        {
            return 0;
        }
        public virtual ChromatogramTimeIntensities ChromatogramData
        {
            get 
            { 
                if (null == Chromatogram)
                {
                    return null;
                }
                var expectedSize = (sizeof (float) + sizeof (float)*NumTransitions)*NumPoints;

                var uncompressedBytes = Chromatogram.Uncompress(expectedSize,false); // don't throw if the uncompressed buffer isn't the size we expected, that's normal here per NickSh


                switch (GetChromatogramFormat())
                {
                    case 0:
                        float[] times;
                        float[][] intensities;

                        short[][] massErrors; // dummy variable
                        int[][] scanIds; // dummy variable
                        ChromatogramCache.BytesToTimeIntensities(uncompressedBytes, NumPoints, NumTransitions,
                            false, false, false, false, // for now, no mass errors or scan IDs (TODO: what about chromatogram libraries for DIA?)
                            out times, out intensities, out massErrors, out scanIds);
                        return new ChromatogramTimeIntensities(times, intensities, massErrors, scanIds);
                    case 1:
                        var rawTimeIntensities = RawTimeIntensities.ReadFromStream(new MemoryStream(uncompressedBytes));
                        var interpolatedTimeIntensities = rawTimeIntensities.Interpolate(Enumerable.Repeat(ChromSource.unknown,
                            rawTimeIntensities.TransitionTimeIntensities.Count));
                        return new ChromatogramTimeIntensities(interpolatedTimeIntensities.InterpolatedTimes.ToArray(),
                            interpolatedTimeIntensities.TransitionTimeIntensities.Select(timeIntensities=>timeIntensities.Intensities.ToArray()).ToArray(),
                            null, null);
                    default:
                        throw new Exception(@"Unknown chromatogram format " + GetChromatogramFormat());
                }
            }
            set
            {
                if (null == value)
                {
                    Chromatogram = null;
                    return;
                }
                var uncompressed = ChromatogramCache.TimeIntensitiesToBytes(value.Times, value.Intensities, value.MassErrors, value.ScanIds);
                Chromatogram = uncompressed.Compress(3);
            }
        }
        public virtual ICollection<Transition> Transitions { get; set; }

        public class ChromatogramTimeIntensities
        {
            public ChromatogramTimeIntensities(float[] times, float[][] intensities, short[][] massErrors, int[][] scanIds)
            {
                Times = times;
                Intensities = intensities;
                MassErrors = massErrors;
                ScanIds = scanIds;
            }
            public float[] Times { get; private set; }
            public float[][] Intensities { get; private set; }
            public short[][] MassErrors { get; private set; }
            public int[][] ScanIds { get; private set; }
        }

        /// <summary>
        /// Schema version 1.2 added "ChromatogramFormat" and "UncompressesSize"
        /// </summary>
        [UsedImplicitly]
        public class Format1Dot2 : Precursor
        {
            protected override int GetChromatogramFormat()
            {
                return ChromatogramFormat;
            }

            public virtual int ChromatogramFormat { get; set; }
            public virtual int UncompressedSize { get; set; }
        }

        /// <summary>
        /// Schema version 1.3 added ion mobility information
        /// </summary>
        [UsedImplicitly]
        public class Format1Dot3 : Format1Dot2
        {
            public virtual string Adduct { get; set; }
            public override Adduct GetAdduct() { return string.IsNullOrEmpty(Adduct) ? base.GetAdduct() : Util.Adduct.FromStringAssumeChargeOnly(Adduct); }
            public virtual double ExplicitIonMobility { get; set; }
            public virtual string ExplicitIonMobilityUnits { get; set; }
            public virtual double ExplicitCcsSqa { get; set; }
            public virtual double ExplicitCompensationVoltage { get; set; }
            public virtual double CCS { get; set; }
            public virtual double IonMobilityMS1 { get; set; }
            public virtual double IonMobilityFragment { get; set; }
            public virtual double IonMobilityWindow { get; set; }
            public virtual string IonMobilityType { get; set; }

            public virtual IonMobilityAndCCS GetIonMobilityAndCCS()
            {
                // Favor any explicit values, since those are what would have been used in chromatogram extraction
                var units = Helpers.ParseEnum(ExplicitIonMobility != 0 ? ExplicitIonMobilityUnits : IonMobilityType, eIonMobilityUnits.none);
                var ionMobility = ExplicitIonMobility != 0 ? ExplicitIonMobility : IonMobilityMS1;
                if (units == eIonMobilityUnits.compensation_V && ExplicitCompensationVoltage != 0)
                {
                    ionMobility = ExplicitCompensationVoltage;
                }
                if (ionMobility == 0)
                {
                    return IonMobilityAndCCS.EMPTY;
                }
                var ionMobilityMS2 = ExplicitIonMobility != 0 ? ExplicitIonMobility : 
                    IonMobilityFragment != 0 ? IonMobilityFragment : ionMobility;
                double? ccs = ExplicitCcsSqa != 0 ? ExplicitCcsSqa : CCS;
                if (ccs == 0)
                {
                    ccs = null;
                }
                double? highEnergyOffset = ionMobilityMS2 - ionMobility;
                return IonMobilityAndCCS.GetIonMobilityAndCCS(ionMobility, units, ccs, highEnergyOffset);
            }
        }
    }
}
