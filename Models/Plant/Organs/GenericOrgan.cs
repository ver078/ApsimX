using System;
using System.Collections.Generic;
using System.Text;

using Models.Core;
using Models.PMF.Functions;
using System.Xml.Serialization;
using Models.PMF.Interfaces;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.Soils.Arbitrator;

namespace Models.PMF.Organs
{

    /// <summary>
    /// This organ is simulated using a generic organ type.
    ///   
    /// **Dry Matter Demands**
    /// A given fraction of daily DM demand is determined to be structural and the remainder is non-structural.
    /// 
    /// **Dry Matter Supplies**
    /// A given fraction of Nonstructural DM is made available to the arbitrator as DMReTranslocationSupply.
    /// 
    /// **Nitrogen Demands**
    /// The daily nonstructural N demand is the product of Total DM demand and a Maximum N concentration less the structural N demand.
    /// The daily structural N demand is the product of Total DM demand and a Minimum N concentration. 
    /// The Nitrogen demand switch is a multiplier applied to nitrogen demand so it can be turned off at certain phases.
    /// 
    /// **Nitrogen Supplies**
    /// As the organ senesces a fraction of senesced N is made available to the arbitrator as NReallocationSupply.
    /// A fraction of nonstructural N is made available to the arbitrator as NRetranslocationSupply
    /// 
    /// **Biomass Senescence and Detachment**
    /// Senescence is calculated as a proportion of the live dry matter.
    /// Detachment of biomass into the surface organic matter pool is calculated daily as a proportion of the dead DM.
    /// 
    /// **Canopy**
    /// The user can model the canopy by specifying either the LAI and an extinction coefficient, or by specifying the canopy cover directly.  If the cover is specified, LAI is calculated using an inverted Beer-Lambert equation with the specified cover value.
    /// 
    /// The canopies values of Cover and LAI are passed to the MicroClimate module which uses the Penman Monteith equation to calculate potential evapotranspiration for each canopy and passes the value back to the crop.
    /// The effect of growth rate on transpiration is captured using the Fractional Growth Rate (FRGR) function which is parameterised as a function of temperature for the simple leaf. 
    ///
    /// </summary>

    [Serializable]
    public class GenericOrgan : Model, IOrgan, IArbitration
    {
        #region Class Parameter Function Links
        /// <summary>The live</summary>
        [Link]
        [DoNotDocument]
        public Biomass Live = null;

        /// <summary>The dead</summary>
        [Link]
        [DoNotDocument]
        public Biomass Dead = null;

        /// <summary>The plant</summary>
        [Link]
        protected Plant Plant = null;

        /// <summary>The surface organic matter model</summary>
        [Link]
        public ISurfaceOrganicMatter SurfaceOrganicMatter = null;

        /// <summary>The summary</summary>
        [Link]
        public ISummary Summary = null;

        /// <summary>The senescence rate function</summary>
        [Link]
        [Units("/d")]
        IFunction SenescenceRate = null;

        /// <summary>The detachment rate function</summary>
        [Link]
        [Units("/d")]
        IFunction DetachmentRateFunction = null;

        /// <summary>The n reallocation factor</summary>
        [Link]
        [Units("/d")]
        IFunction NReallocationFactor = null;

        /// <summary>The n retranslocation factor</summary>
        [Link(IsOptional = true)]
        [Units("/d")]
        IFunction NRetranslocationFactor = null;
        /// <summary>The nitrogen demand switch</summary>
        [Link(IsOptional = true)]
        IFunction NitrogenDemandSwitch = null;
        /// <summary>The dm retranslocation factor</summary>
        [Link(IsOptional = true)]
        [Units("/d")]
        IFunction DMRetranslocationFactor = null;
        /// <summary>The structural fraction</summary>
        [Link]
        [Units("g/g")]
        IFunction StructuralFraction = null;
        /// <summary>The dm demand Function</summary>
        [Link]
        [Units("g/m2/d")]
        IFunction DMDemandFunction = null;
        /// <summary>The initial wt function</summary>
        [Link]
        [Units("g/m2")]
        IFunction InitialWtFunction = null;
        /// <summary>The maximum n conc</summary>
        [Link]
        [Units("g/g")]
        public IFunction MaximumNConc = null;
        /// <summary>The minimum n conc</summary>
        [Units("g/g")]
        [Link]
        public IFunction MinimumNConc = null;
        /// <summary>The proportion of biomass repired each day</summary>
        [Link(IsOptional = true)]
        public IFunction MaintenanceRespirationFunction = null;
        /// <summary>Dry matter conversion efficiency</summary>
        [Link(IsOptional = true)]
        public IFunction DMConversionEfficiencyFunction = null;
        #endregion

        #region States
        /// <summary>The start n retranslocation supply</summary>
        private double StartNRetranslocationSupply = 0;
        /// <summary>The start n reallocation supply</summary>
        private double StartNReallocationSupply = 0;
        /// <summary>The potential dm allocation</summary>
        protected double PotentialDMAllocation = 0;
        /// <summary>The potential structural dm allocation</summary>
        protected double PotentialStructuralDMAllocation = 0;
        /// <summary>The potential metabolic dm allocation</summary>
        protected double PotentialMetabolicDMAllocation = 0;
        /// <summary>The structural dm demand</summary>
        protected double StructuralDMDemand = 0;
        /// <summary>The non structural dm demand</summary>
        protected double NonStructuralDMDemand = 0;
        /// <summary>The start live</summary>
        private Biomass StartLive = new Biomass();

        /// <summary>Clears this instance.</summary>
        protected virtual void Clear()
        {
            Live.Clear();
            Dead.Clear();
            StartNRetranslocationSupply = 0;
            StartNReallocationSupply = 0;
            PotentialDMAllocation = 0;
            PotentialStructuralDMAllocation = 0;
            PotentialMetabolicDMAllocation = 0;
            StructuralDMDemand = 0;
            NonStructuralDMDemand = 0;
        }
        #endregion

        #region Class properties

        /// <summary>Gets the dm amount detached (sent to soil/surface organic matter) (g/m2)</summary>
        [XmlIgnore]
        public double DetachedWt { get; set; }

        /// <summary>Gets the N amount detached (sent to soil/surface organic matter) (g/m2)</summary>
        [XmlIgnore]
        public double DetachedN { get; set; }

        /// <summary>Gets the DM amount removed from the system (harvested, grazed, etc) (g/m2)</summary>
        [XmlIgnore]
        public double RemovedWt { get; set; }

        /// <summary>Gets the N amount removed from the system (harvested, grazed, etc) (g/m2)</summary>
        [XmlIgnore]
        public double RemovedN { get; set; }

        /// <summary>Gets the dm supply photosynthesis.</summary>
        [Units("g/m^2")]
        virtual public double DMSupplyPhotosynthesis { get { return DMSupply.Fixation; } }

        /// <summary>The amount of mass lost each day from maintenance respiration</summary>
        [XmlIgnore]
        virtual public double MaintenanceRespiration { get; private set; }

        /// <summary>the efficiency with which allocated DM is converted to organ mass.</summary>
        [XmlIgnore]
        virtual public double DMConversionEfficiency { get { return 1; } set { } }

        /// <summary>Growth Respiration</summary>
        [XmlIgnore]
        public double GrowthRespiration { get; set; }

        #endregion

        #region Organ functions
        /// <summary>Removes biomass from organs when harvest, graze or cut events are called.</summary>
        /// <param name="value">The fractions of biomass to remove</param>
        virtual public void DoRemoveBiomass(OrganBiomassRemovalType value)
        {
            double totalFractionToRemove = value.FractionLiveToRemove + value.FractionDeadToRemove
                                           + value.FractionLiveToResidue + value.FractionDeadToResidue;
            
            if (totalFractionToRemove > 0.0)
            {
                double RemainingLiveFraction = 1.0 - (value.FractionLiveToResidue + value.FractionLiveToRemove);
                double RemainingDeadFraction = 1.0 - (value.FractionDeadToResidue + value.FractionDeadToRemove);

                double detachingWt = Live.Wt * value.FractionLiveToResidue + Dead.Wt * value.FractionDeadToResidue;
                double detachingN = Live.N * value.FractionLiveToResidue + Dead.N * value.FractionDeadToResidue;
                RemovedWt += Live.Wt * value.FractionLiveToRemove + Dead.Wt * value.FractionDeadToRemove;
                RemovedN += Live.N * value.FractionLiveToRemove + Dead.N * value.FractionDeadToRemove;
                DetachedWt += detachingWt;
                DetachedN += detachingN;

                Live.StructuralWt *= RemainingLiveFraction;
                Live.NonStructuralWt *= RemainingLiveFraction;
                Live.MetabolicWt *= RemainingLiveFraction;
                Dead.StructuralWt *= RemainingDeadFraction;
                Dead.NonStructuralWt *= RemainingDeadFraction;
                Dead.MetabolicWt *= RemainingDeadFraction;

                Live.StructuralN *= RemainingLiveFraction;
                Live.NonStructuralN *= RemainingLiveFraction;
                Live.MetabolicN *= RemainingLiveFraction;
                Dead.StructuralN *= RemainingDeadFraction;
                Dead.NonStructuralN *= RemainingDeadFraction;
                Dead.MetabolicN *= RemainingDeadFraction;

                SurfaceOrganicMatter.Add(detachingWt * 10, detachingN * 10, 0.0, Plant.CropType, Name);
                //TODO: theoretically the dead material is different from the live, so it should be added as a separate pool to SurfaceOM

                double toResidue = (value.FractionLiveToResidue + value.FractionDeadToResidue) / totalFractionToRemove * 100;
                double removedOff = (value.FractionLiveToRemove + value.FractionDeadToRemove) / totalFractionToRemove * 100;
                Summary.WriteMessage(this, "Removing " + (totalFractionToRemove * 100).ToString("0.0")
                                         + "% of " + Name + " Biomass from " + Plant.Name
                                         + ".  Of this " + removedOff.ToString("0.0") + "% is removed from the system and "
                                         + toResidue.ToString("0.0") + "% is returned to the surface organic matter");
            }
        }
        #endregion

        #region Arbitrator methods

        /// <summary>Gets or sets the dm demand.</summary>
        [XmlIgnore]
        [Units("g/m^2")]
        public virtual BiomassPoolType DMDemand
        {
            get
            {
                StructuralDMDemand = DMDemandFunction.Value * StructuralFraction.Value/DMConversionEfficiency;
                double MaximumDM = (StartLive.StructuralWt + StructuralDMDemand) * 1 / StructuralFraction.Value;
                MaximumDM = Math.Min(MaximumDM, 10000); // FIXME-EIT Temporary solution: Cealing value of 10000 g/m2 to ensure that infinite MaximumDM is not reached when 0% goes to structural fraction   
                NonStructuralDMDemand = Math.Max(0.0, MaximumDM - StructuralDMDemand - StartLive.StructuralWt - StartLive.NonStructuralWt);
                NonStructuralDMDemand /= DMConversionEfficiency;

                return new BiomassPoolType { Structural = StructuralDMDemand, NonStructural = NonStructuralDMDemand };
            }
            set { }
        }
        /// <summary>Sets the dm potential allocation.</summary>
        [XmlIgnore]
        public BiomassPoolType DMPotentialAllocation
        {
            set
            {
                if (DMDemand.Structural == 0)
                    if (value.Structural < 0.000000000001) { }//All OK
                    else
                        throw new Exception("Invalid allocation of potential DM in " + Name);
                PotentialMetabolicDMAllocation = value.Metabolic;
                PotentialStructuralDMAllocation = value.Structural;
                PotentialDMAllocation = value.Structural + value.Metabolic;
            }
        }

        /// <summary>Gets or sets the dm supply.</summary>
        [XmlIgnore]
        public virtual BiomassSupplyType DMSupply
        {
            get
            {
                return new BiomassSupplyType
                {
                    Fixation = 0.0,
                    Retranslocation = AvailableDMRetranslocation(),
                    Reallocation = 0.0
                };
            }
            set { }
        }

        /// <summary>Gets the amount of DM available for retranslocation</summary>
        /// <returns>DM available to retranslocate</returns>
        public double AvailableDMRetranslocation()
        {
            if (DMRetranslocationFactor != null)
                return StartLive.NonStructuralWt * DMRetranslocationFactor.Value;
            else
            { //Default of 0 means retranslocation is always turned off!!!!
                return 0.0;
            }
        }

        /// <summary>Gets or sets the N demand.</summary>
        [XmlIgnore]
        public virtual BiomassPoolType NDemand
        {
            get
            {
                double _NitrogenDemandSwitch = 1;
                if (NitrogenDemandSwitch != null) //Default of 1 means demand is always truned on!!!!
                    _NitrogenDemandSwitch = NitrogenDemandSwitch.Value;
                double NDeficit = Math.Max(0.0, MaximumNConc.Value * (Live.Wt + PotentialDMAllocation) - Live.N);
                NDeficit *= _NitrogenDemandSwitch;
                double StructuralNDemand = Math.Min(NDeficit, PotentialStructuralDMAllocation * MinimumNConc.Value);
                double NonStructuralNDemand = Math.Max(0, NDeficit - StructuralNDemand);
                return new BiomassPoolType { Structural = StructuralNDemand, NonStructural = NonStructuralNDemand };
            }
            set { }
        }
        /// <summary>Gets or sets the N supply.</summary>
        [XmlIgnore]
        public virtual BiomassSupplyType NSupply
        {
            get
            {
                return new BiomassSupplyType()
                {
                    Reallocation = AvailableNReallocation(),
                    Retranslocation = AvailableNRetranslocation(),
                    Uptake = 0.0
                };
            }
            set { }
        }

        /// <summary>Gets or sets the n fixation cost.</summary>
        [XmlIgnore]
        public virtual double NFixationCost { get { return 0; } set { } }

        /// <summary>Gets the N amount available for retranslocation</summary>
        /// <returns>N available to retranslocate</returns>
        public double AvailableNRetranslocation()
        {
            if (NRetranslocationFactor != null)
            {
                double LabileN = Math.Max(0, StartLive.NonStructuralN - StartLive.NonStructuralWt * MinimumNConc.Value);
                return (LabileN - StartNReallocationSupply) * NRetranslocationFactor.Value;
            }
            else
            {
                //Default of 0 means retranslocation is always turned off!!!!
                return 0.0;
            }
        }

        /// <summary>Gets the N amount available for reallocation</summary>
        /// <returns>DM available to reallocate</returns>
        public double AvailableNReallocation()
        {
            return SenescenceRate.Value * StartLive.NonStructuralN * NReallocationFactor.Value;
        }

        /// <summary>Sets the dm allocation.</summary>
        [XmlIgnore]
        public virtual BiomassAllocationType DMAllocation
        {
            set
            {
                GrowthRespiration = 0;
                GrowthRespiration += value.Structural * (1-DMConversionEfficiency)
                                   + value.NonStructural * (1-DMConversionEfficiency);
                
                Live.StructuralWt += Math.Min(value.Structural* DMConversionEfficiency, StructuralDMDemand);
                
                // Excess allocation
                if (value.NonStructural < -0.0000000001)
                    throw new Exception("-ve NonStructuralDM Allocation to " + Name);
                if ((value.NonStructural*DMConversionEfficiency - DMDemand.NonStructural) > 0.0000000001)
                    throw new Exception("Non StructuralDM Allocation to " + Name + " is in excess of its Capacity");
                if (DMDemand.NonStructural > 0)
                    Live.NonStructuralWt += value.NonStructural * DMConversionEfficiency;

                // Retranslocation
                if (value.Retranslocation - StartLive.NonStructuralWt > 0.0000000001)
                    throw new Exception("Retranslocation exceeds nonstructural biomass in organ: " + Name);
                Live.NonStructuralWt -= value.Retranslocation;
            }
        }
        /// <summary>Sets the n allocation.</summary>
        [XmlIgnore]
        public virtual BiomassAllocationType NAllocation
        {
            set
            {
                Live.StructuralN += value.Structural;
                Live.NonStructuralN += value.NonStructural;

                // Retranslocation
                if (MathUtilities.IsGreaterThan(value.Retranslocation, StartLive.NonStructuralN - StartNRetranslocationSupply))
                    throw new Exception("N Retranslocation exceeds nonstructural nitrogen in organ: " + Name);
                if (value.Retranslocation < -0.000000001)
                    throw new Exception("-ve N Retranslocation requested from " + Name);
                Live.NonStructuralN -= value.Retranslocation;

                // Reallocation
                if (MathUtilities.IsGreaterThan(value.Reallocation, StartLive.NonStructuralN))
                    throw new Exception("N Reallocation exceeds nonstructural nitrogen in organ: " + Name);
                if (value.Reallocation < -0.000000001)
                    throw new Exception("-ve N Reallocation requested from " + Name);
                Live.NonStructuralN -= value.Reallocation;
            }
        }

        /// <summary>Gets or sets the maximum nconc.</summary>
        public double MaxNconc { get { return MaximumNConc.Value; } }
        /// <summary>Gets or sets the minimum nconc.</summary>
        public double MinNconc { get { return MinimumNConc.Value; } }

        /// <summary>Gets the total (live + dead) dm (g/m2)</summary>
        public double Wt { get { return Live.Wt + Dead.Wt; } }

        /// <summary>Gets the total (live + dead) n (g/m2)</summary>
        public double N { get { return Live.N + Dead.N; } }

        #endregion

        #region Events and Event Handlers
        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        /// 
        [EventSubscribe("Commencing")]
        protected void OnSimulationCommencing(object sender, EventArgs e)
        {
            Clear();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        protected void OnPlantSowing(object sender, SowPlant2Type data)
        {
            if (data.Plant == Plant)
                Clear();

            if (DMConversionEfficiencyFunction != null)
                DMConversionEfficiency = DMConversionEfficiencyFunction.Value;
            else
                DMConversionEfficiency = 1.0;
        }

        /// <summary>Called when crop is emerging</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">Event data</param>
        [EventSubscribe("PlantEmerging")]
        protected void OnPlantEmerging(object sender, EventArgs e)
        {
            //Initialise biomass and nitrogen
            Live.StructuralWt = InitialWtFunction.Value;
            Live.NonStructuralWt = 0.0;
            Live.StructuralN = Live.StructuralWt * MinimumNConc.Value;
            Live.NonStructuralN = (InitialWtFunction.Value * MaximumNConc.Value) - Live.StructuralN;
        }

        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        protected void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            if (Plant.IsEmerged)
            {
                StartLive = Live;
                StartNReallocationSupply = NSupply.Reallocation;
                StartNRetranslocationSupply = NSupply.Retranslocation;
            }
        }

        /// <summary>Does the nutrient allocations.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoActualPlantGrowth")]
        protected void OnDoActualPlantGrowth(object sender, EventArgs e)
        {
            if (Plant.IsAlive)
            {
                // Do senescence
                Biomass Loss = Live * SenescenceRate.Value;
                Live.Subtract(Loss);
                Dead.Add(Loss);

                // Do detachment
                double DetachedFrac = DetachmentRateFunction.Value;
                Biomass detaching = Dead * DetachedFrac;
                Dead.Multiply(1 - DetachedFrac);
                if (detaching.Wt > 0.0)
                {
                    DetachedWt += detaching.Wt;
                    DetachedN += detaching.N;
                    SurfaceOrganicMatter.Add(detaching.Wt * 10, detaching.N * 10, 0, Plant.CropType, Name);
                }

                // Do maintenance respiration
                MaintenanceRespiration = 0;
                if (MaintenanceRespirationFunction != null)
                {
                    MaintenanceRespiration += Live.MetabolicWt * MaintenanceRespirationFunction.Value;
                    Live.MetabolicWt *= (1 - MaintenanceRespirationFunction.Value);
                    MaintenanceRespiration += Live.NonStructuralWt * MaintenanceRespirationFunction.Value;
                    Live.NonStructuralWt *= (1 - MaintenanceRespirationFunction.Value);
                }
            }
        }


        /// <summary>Called when crop is ending</summary>
        ///[EventSubscribe("PlantEnding")]
        [EventSubscribe("PlantEnding")]
        private void DoPlantEnding(object sender, EventArgs e)
        {
            if (Wt > 0.0)
            {
                DetachedWt += Wt;
                DetachedN += N;
                SurfaceOrganicMatter.Add(Wt * 10, N * 10, 0, Plant.CropType, Name);
            }

            Clear();
        }

        #endregion
    }
}
