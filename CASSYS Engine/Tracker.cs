﻿// CASSYS - Grid connected PV system modelling software 
// (c) Canadian Solar Solutions Inc.
///////////////////////////////////////////////////////////////////////////////
//
// Title: Tracker.cs
// 
// Revision History:
// AP - 2016-01-26: Version 0.9.3
// NB - 2016-02-17: Updated equations for Tilt and Roll tracker
// NB - 2016-02-24: Addition of backtracking options for horizontal axis cases
// DT - 2018-06-26: Calculation of tracker angle for E-W and N-S trackers
//
// Description:
// This class is reponsible for the simulation of trackers, specifically the 
// following types:
//  1)	Single Axis Elevation Tracker (E-W, N-S axis), and Tilt and Roll Tracker - SAXT
//  2)	Azimuth or Vertical Axis Tracker - AVAT
//  3)	Two-Axis Tracking - TAXT
//  4)  No Axis Tracking - NOAT
//
///////////////////////////////////////////////////////////////////////////////
// References and Supporting Documentation or Links
///////////////////////////////////////////////////////////////////////////////
// Solar geometry for fixed and tracking surfaces - Braun and Mitchell
//          Solar Energy, Vol. 31, No. 5, pp. 439-444, 1983
//
// Rotation Angle for the Optimum Tracking of One-Axis Trackers - Marion and Dobos
//          NREL, http://www.nrel.gov/docs/fy13osti/58891.pdf
//
// Tracking and back-tracking - Lorenzo, Narvarte, and Munoz
//          Progress in Photovoltaics: Research and Applications, Vol. 19,
//          Issue 6, pp. 747-753, 2011
//
///////////////////////////////////////////////////////////////////////////////
// Notes
///////////////////////////////////////////////////////////////////////////////
// Note 1: NB
// The 360 degree (2PI) correction for the surface azimuth of the tilt and roll
// tracker is due to a new set of equations that were used for the tilt and roll
// trackers. These equations were from the NREL paper. In this paper the azimuth
// was defined between 0 and 360 degrees clockwise from north, whereas CASSYS
// uses an azimuth input of 0 being true south and the angle is negative in the
// east and positive in the west, with +/-180 being north. The equations work
// well except for when the tracker azimuth is set to greater than the absolute
// value of 90 degrees, as in some circumstances the equations output values
// either greater than 180 degrees when the axis azimuth is greater than 90 or
// less than -180 degrees when the axis azimuth is less than -90. This is due to
// the difference between the way the paper defines azimuth and the way CASSYS
// defines azimuth. To correct this 360 degrees is either added to or subtracted
// from the azimuth if the azimuth is greater than 180 or less than -180 to put
// the given angle into a quadrant useable by CASSYS while keeping the angle
// equivalent to the angle output by the equation.
//
///////////////////////////////////////////////////////////////////////////////



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;

namespace CASSYS
{
    public enum TrackMode { NOAT, SAXT, AVAT, TAXT, FTSA }             // See description in header for expanded form.


    class Tracker
    {
        // Local variables
        // Tracker Variables (Made public to write to output file)
        public TrackMode itsTrackMode;
        public double itsTrackerSlope;
        public double itsTrackerAzimuth;

        // Operational Limits (as they apply to the surface, typically)
        double itsMinTilt;					// Min. slope of tracker surface with respect to horizontal [radians]
        double itsMaxTilt;					// Max. slope of tracker surface with respect to horizontal [radians]
        double itsMinRotationAngle;			// Min. angle of rotation of surface about tracker axis [radians]
        double itsMaxRotationAngle;			// Max. angle of rotation of surface about tracker axis [radians]
        double itsMinAzimuth;				// Min. angle of horizontal proj. of normal to module surface and true South [radians]
        double itsMaxAzimuth;				// Max. angle of horizontal proj. of normal to module surface and true South [radians]
        double itsAzimuthRef;               // Describes whether the tracker is in the northern or southern hemisphere
        public bool useBackTracking;	    // Boolean used to determine if backtracking is enabled
        public bool useBifacial;            // Boolean used to determine if panels are bifacial
        public double itsTrackerPitch;		// Distance between two rows of trackers [m]
        public double itsTrackerBW;			// Width of single tracker array [m]
        public double itsTrackerClearance;  // Array ground clearance; for trackers with dynamic tilt, measured at tilt = 0 degrees [m]

        // Output Variables
        public double SurfSlope;			// Angle of tracker surface with respect to horizontal [radians]
        public double SurfAzimuth;			// Angle between horizontal projection of normal to module surface and true South [radians]
        public double SurfClearance;        // Array ground clearance: distance from lowest point on array surface to ground [m]
        public double IncidenceAngle;       // Angle of a ray of light incident on the normal of panel surface [radians]
        public double RotAngle;				// Angle of rotation of surface about tracker axis [radians]
        public double AngleCorrection;      // With backtracking enabled, this angle adjustment is applied to the surface slope angle [radians]

        // Variables used for "Fixed Tilted Plane Seasonal Adjustment" orientation mode
        int itsSummerMonth;                 // Month in which meter tilt is adjusted for summer
        int itsWinterMonth;                 // Month in which meter tilt is adjusted for winter
        int itsSummerDay;                   // Day in which meter tilt is adjusted for summer
        int itsWinterDay;                   // Day in which meter tilt is adjusted for winter
        double itsPlaneTiltSummer;          // Tilt used during summer timespan
        double itsPlaneTiltWinter;          // Tilt used during winter timespan
        DateTime SummerDate;                // Holds Year, Month, and Day of summer date
        DateTime WinterDate;                // Holds Year, Month, and Day of winter date
        int previousYear = 0;               // Holds the year of the previous timestamp


        // Constructor for the tracker
        public Tracker()
        {
        }


        // Calculate the tracker slope, azimuth and incidence angle using
        public void Calculate
            (
              double SunZenith
            , double SunAzimuth
            , int Year
            , int DayOfYear
            )
        {
            switch (itsTrackMode)
            {
                case TrackMode.SAXT:
                    // Surface stays parallel to the ground.
                    if (itsTrackerSlope == 0.0)
                    {
                        // For east-west tracking, the absolute value of the sun-azimuth is checked against the tracker azimuth
                        // This is from Duffie and Beckman Page 22.
                        if (itsTrackerAzimuth == Math.PI / 2 || itsTrackerAzimuth == -Math.PI/2)
                        {
                            // If the user inputs a minimum tilt less than 0, the tracker is able to face the non-dominant direction, so the surface azimuth will change based on the sun azimuth.
                            // However, if the minimum tilt is greater than zero, the tracker can only face the dominant direction.
                            if (itsMinTilt <= 0)
                            {
                                // Math.Abs is used so that the surface azimuth is set to 0 degrees if the sun azimuth is between -90 and 90, and set to 180 degrees if the sun azimuth is between -180 and -90 or between 90 and 180
                                if (Math.Abs(SunAzimuth) >= Math.Abs(itsTrackerAzimuth))
                                {
                                    SurfAzimuth = Math.PI;
                                }
                                else
                                {
                                    SurfAzimuth = 0;
                                }
                            }
                            else
                            {
                                SurfAzimuth = itsTrackerAzimuth - Math.PI / 2;
                            }
                        }
                        else if (itsTrackerAzimuth == 0)
                        {
                            // For north-south tracking, the sign of the sun-azimuth is checked against the tracker azimuth
                            // This is from Duffie and Beckman Page 22.
                            if (SunAzimuth >= itsTrackerAzimuth)
                            {
                                SurfAzimuth = Math.PI / 2;                            
                            }
                            else
                            {
                                SurfAzimuth = -Math.PI / 2;
                            }
                        }
						
						// Surface slope calculated from eq. 31 of reference guide
                        SurfSlope = Math.Atan2(Math.Sin(SunZenith) * Math.Cos(SurfAzimuth - SunAzimuth), Math.Cos(SunZenith));
 
                        // If the shadow is greater than the Pitch and backtracking is selected
                        if (useBackTracking)
                        {
                            if (itsTrackerBW / (Math.Cos(SurfSlope)) > itsTrackerPitch)
                            {
                                // NB: From Lorenzo, Narvarte, and Munoz
                                AngleCorrection = Math.Acos((itsTrackerPitch * (Math.Cos(SurfSlope))) / itsTrackerBW);
                                SurfSlope = SurfSlope - AngleCorrection;
                            }
                        }

                        // Adjusting limits for elevation tracking, so if positive min tilt, the tracker operates within limits properly
                        if (itsTrackerAzimuth == Math.PI / 2 || itsTrackerAzimuth == -Math.PI / 2)
                        {
                            if (itsMinTilt <= 0)
                            {
                                if (Math.Abs(SunAzimuth) <= itsTrackerAzimuth)
                                {
                                    SurfSlope = Math.Min(itsMaxTilt, SurfSlope);
                                }
                                else if (Math.Abs(SunAzimuth) > itsTrackerAzimuth)
                                {
                                    SurfSlope = Math.Min(Math.Abs(itsMinTilt), SurfSlope);
                                }
                            }

                            else if (itsMinTilt > 0)
                            {
                                SurfSlope = Math.Min(SurfSlope, itsMaxTilt);
                                SurfSlope = Math.Max(SurfSlope, itsMinTilt);
                            }
                        }

                        else if (itsTrackerAzimuth == 0)
                        {
                            SurfSlope = Math.Min(itsMaxTilt, SurfSlope);
                        }

                        // For all single axis trackers in E-W or N-S position, rotation of tracker is same as slope, except for sign
                        RotAngle = SurfSlope;
                        // N-S tracker
                        if (itsTrackerAzimuth == 0)
                            RotAngle *= Math.Sign(SurfAzimuth);                                          // Same convention as azimuth (negative for East-facing)
                        // E-W tracker
                        else if (itsTrackerAzimuth == Math.PI / 2 || itsTrackerAzimuth == -Math.PI / 2)
                            if (SurfAzimuth > Math.PI / 2) RotAngle = -RotAngle;                         // Negative for North-facing

                    }
                    else
                    {
                        // Tilt and Roll
                        double aux = Tilt.GetCosIncidenceAngle(SunZenith, SunAzimuth, itsTrackerSlope, itsTrackerAzimuth);
                        // Equation (7) from Marion and Dobos
                        RotAngle =  Math.Atan2((Math.Sin(SunZenith) * Math.Sin(SunAzimuth - itsTrackerAzimuth)), aux);

                        //NB: enforcing rotation limits on tracker
                        RotAngle = Math.Min(itsMaxRotationAngle, RotAngle);
                        RotAngle = Math.Max(itsMinRotationAngle, RotAngle);


                        // Slope from equation (1) in Marion and Dobos
                        SurfSlope = Math.Acos(Math.Cos(RotAngle) * Math.Cos(itsTrackerSlope));

                        // Surface Azimuth from NREL paper
                        if (SurfSlope != 0)
                        {
                            // Equation (3) in Marion and Dobos
                            if ((-Math.PI <= RotAngle) && (RotAngle < -Math.PI/2))
                            {
                                SurfAzimuth = itsTrackerAzimuth - Math.Asin(Math.Sin(RotAngle) / Math.Sin(SurfSlope)) - Math.PI;
                            }
                            // Equation (4) in Marion and Dobos
                            else if ((Math.PI / 2 < RotAngle) && (RotAngle <= Math.PI))
                            {
                                SurfAzimuth = itsTrackerAzimuth - Math.Asin(Math.Sin(RotAngle) / Math.Sin(SurfSlope)) + Math.PI;
                            }
                            // Equation (2) in Marion and Dobos
                            else
                            {
                                SurfAzimuth = itsTrackerAzimuth + Math.Asin(Math.Sin(RotAngle) / Math.Sin(SurfSlope));
                            }
                        }
                        //NB: 360 degree correction to put Surface Azimuth into the correct quadrant, see Note 1
                        if (SurfAzimuth > Math.PI)
                        {
                            SurfAzimuth -= (Math.PI) * 2;
                        }
                        else if (SurfAzimuth < -Math.PI)
                        {
                            SurfAzimuth += (Math.PI) * 2;
                        }
                    }
                    if (useBifacial)
                    {
                        SurfClearance = itsTrackerClearance - (itsTrackerBW * Math.Sin(SurfSlope)) / 2;

                        if (SurfClearance < 0)
                        {
                            ErrorLogger.Log("Tracker surface ground clearance cannot be negative. Check the maximum rotation angle and ground clearance values.", ErrLevel.FATAL);
                        }
                    }
                    break;

                // Two Axis Tracking
                case TrackMode.TAXT:
                    // Defining the surface slope
                    SurfSlope = SunZenith;
                    SurfSlope = Math.Max(itsMinTilt, SurfSlope);
                    SurfSlope = Math.Min(itsMaxTilt, SurfSlope);

                    // Defining the surface azimuth
                    SurfAzimuth = SunAzimuth;
                    
                    // Changes the reference frame to be with respect to the reference azimuth
                    if (SurfAzimuth >= 0)
                    {
                        SurfAzimuth -= itsAzimuthRef;
                    }
                    else
                    {
                        SurfAzimuth += itsAzimuthRef;
                    }

                    // Enforcing the rotation limits with respect to the reference azimuth
                    SurfAzimuth = Math.Max(itsMinAzimuth, SurfAzimuth);
                    SurfAzimuth = Math.Min(itsMaxAzimuth, SurfAzimuth);

                    // Moving the surface azimuth back into the azimuth variable convention
                    if (SurfAzimuth >= 0)
                    {
                        SurfAzimuth -= itsAzimuthRef;
                    }

                    else
                    {
                        SurfAzimuth += itsAzimuthRef;
                    }
                    break;

                // Azimuth Vertical Axis Tracking
                case TrackMode.AVAT:
                    // Slope is constant.
                    // Defining the surface azimuth
                    SurfAzimuth = SunAzimuth;

                    // Changes the reference frame to be with respect to the reference azimuth
                    if (SurfAzimuth >= 0)
                    {
                        SurfAzimuth -= itsAzimuthRef;
                    }
                    else
                    {
                        SurfAzimuth += itsAzimuthRef;
                    }

                    // Enforcing the rotation limits with respect to the reference azimuth
                    SurfAzimuth = Math.Max(itsMinAzimuth, SurfAzimuth);
                    SurfAzimuth = Math.Min(itsMaxAzimuth, SurfAzimuth);

                    // Moving the surface azimuth back into the azimuth variable convention
                    if (SurfAzimuth >= 0)
                    {
                        SurfAzimuth -= itsAzimuthRef;
                    }

                    else
                    {
                        SurfAzimuth += itsAzimuthRef;
                    }

                    break;

                // Fixed Tilt with Seasonal Adjustment
                // determining if the current timestamp is in the summer or winter season and setting SurfSlope accordingly
                case TrackMode.FTSA:
                    // SummerDate and WinterDate must be recalculated if year changes due to possible leap year
                    if (previousYear != Year)
                    {
                        SummerDate = new DateTime(Year, itsSummerMonth, itsSummerDay);
                        WinterDate = new DateTime(Year, itsWinterMonth, itsWinterDay);
                    }
                    previousYear = Year;
                    
                    // Winter date is before summer date in calender year
                    if (SummerDate.DayOfYear - WinterDate.DayOfYear > 0)
                    {
                        if (DayOfYear >= WinterDate.DayOfYear && DayOfYear < SummerDate.DayOfYear)
                        {
                            SurfSlope = itsPlaneTiltWinter;
                        }
                        else
                        {
                            SurfSlope = itsPlaneTiltSummer;
                        }
                    }
                    // Summer date is before winter date in calender year
                    else
                    {
                        if (DayOfYear >= SummerDate.DayOfYear && DayOfYear < WinterDate.DayOfYear)
                        {
                            SurfSlope = itsPlaneTiltSummer;
                        }
                        else
                        {
                            SurfSlope = itsPlaneTiltWinter;
                        }
                    }
                    break;

                case TrackMode.NOAT:
                    break;
                // Throw error to user if there is an issue with the tracker.
                default:
                    ErrorLogger.Log("Tracking Parameters were incorrectly defined. Please check your input file.", ErrLevel.FATAL);
                    break;
            }

            IncidenceAngle = Tilt.GetIncidenceAngle(SunZenith, SunAzimuth, SurfSlope, SurfAzimuth);
        }
        // Gathering the tracker mode, and relevant operational limits, and tracking axis characteristics.
        public void Config()
        {
            useBifacial = Convert.ToBoolean(ReadFarmSettings.GetInnerText("Bifacial", "UseBifacialModel", ErrLevel.FATAL));

            switch (ReadFarmSettings.GetAttribute("O&S", "ArrayType", ErrLevel.FATAL))
            {
                case "Fixed Tilted Plane":
                    itsTrackMode = TrackMode.NOAT;
                    if (String.Compare(ReadFarmSettings.CASSYSCSYXVersion,"0.9.3") >= 0)
                    {
                        SurfSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PlaneTiltFix", ErrLevel.FATAL));
                        SurfAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AzimuthFix", ErrLevel.FATAL));
                    }
                    else
                    {
                        SurfSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PlaneTilt", ErrLevel.FATAL));
                        SurfAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "Azimuth", ErrLevel.FATAL));
                    }

                    if (useBifacial)
                    {
                        SurfClearance = Convert.ToDouble(ReadFarmSettings.GetInnerText("Bifacial", "GroundClearance", ErrLevel.FATAL));
                    }
                    break;

                case "Fixed Tilted Plane Seasonal Adjustment":
                    itsTrackMode = TrackMode.FTSA;
                    SurfAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AzimuthSeasonal", ErrLevel.FATAL));
                    itsSummerMonth = DateTime.ParseExact(ReadFarmSettings.GetInnerText("O&S", "SummerMonth", _Error: ErrLevel.FATAL), "MMM", CultureInfo.CurrentCulture).Month;
                    itsWinterMonth = DateTime.ParseExact(ReadFarmSettings.GetInnerText("O&S", "WinterMonth", _Error: ErrLevel.FATAL), "MMM", CultureInfo.CurrentCulture).Month;
                    itsSummerDay = int.Parse(ReadFarmSettings.GetInnerText("O&S", "SummerDay", _Error: ErrLevel.FATAL));
                    itsWinterDay = int.Parse(ReadFarmSettings.GetInnerText("O&S", "WinterDay", _Error: ErrLevel.FATAL));
                    itsPlaneTiltSummer = Util.DTOR * double.Parse(ReadFarmSettings.GetInnerText("O&S", "PlaneTiltSummer", _Error: ErrLevel.FATAL));
                    itsPlaneTiltWinter = Util.DTOR * double.Parse(ReadFarmSettings.GetInnerText("O&S", "PlaneTiltWinter", _Error: ErrLevel.FATAL));

                    // Assume the simualtion will begin when the array is in the summer tilt
                    SurfSlope = itsPlaneTiltSummer;

                    break;

                case "Unlimited Rows":
                    itsTrackMode = TrackMode.NOAT;
                    // Defining all the parameters for the shading of a unlimited row array configuration
                    SurfSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PlaneTilt", ErrLevel.FATAL));
                    SurfAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "Azimuth", ErrLevel.FATAL));

                    if (useBifacial)
                    {
                        SurfClearance = Convert.ToDouble(ReadFarmSettings.GetInnerText("Bifacial", "GroundClearance", ErrLevel.FATAL));
                    }
                    break;

                case "Single Axis Elevation Tracking (E-W)":
                    // Tracker Parameters
                    itsTrackMode = TrackMode.SAXT;
                    itsTrackerSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AxisTiltSAET", ErrLevel.FATAL));
                    itsTrackerAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AxisAzimuthSAET", ErrLevel.FATAL));

                    // Operational Limits
                    itsMinTilt = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MinTiltSAET", ErrLevel.FATAL));
                    itsMaxTilt = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MaxTiltSAET", ErrLevel.FATAL));
                    
                    // Backtracking Options
                    useBackTracking = Convert.ToBoolean(ReadFarmSettings.GetInnerText("O&S", "BacktrackOptSAET", ErrLevel.WARNING, _default: "false"));

                    if (useBackTracking)
                    {
                        itsTrackerPitch = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PitchSAET", ErrLevel.FATAL));
                        itsTrackerBW = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "WActiveSAET", ErrLevel.FATAL));
                    }

                    // Bifacial Options
                    if (useBifacial)
                    {
                        itsTrackerClearance = Convert.ToDouble(ReadFarmSettings.GetInnerText("Bifacial", "GroundClearance", ErrLevel.FATAL));
                        itsTrackerPitch = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PitchSAET", ErrLevel.FATAL));
                        itsTrackerBW = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "WActiveSAET", ErrLevel.FATAL));
                    }
                    break;

                case "Single Axis Horizontal Tracking (N-S)":
                    // Tracker Parameters
                    itsTrackMode = TrackMode.SAXT;
                    itsTrackerSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AxisTiltSAST", ErrLevel.FATAL));
                    itsTrackerAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AxisAzimuthSAST", ErrLevel.FATAL));

                    // Operational Limits
                    itsMaxTilt = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "RotationMaxSAST", ErrLevel.FATAL));
                    
                    // Backtracking Options
                    useBackTracking = Convert.ToBoolean(ReadFarmSettings.GetInnerText("O&S", "BacktrackOptSAST", ErrLevel.WARNING, _default: "false"));

                    if (useBackTracking)
                    {
                        itsTrackerPitch = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PitchSAST", ErrLevel.FATAL));
                        itsTrackerBW = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "WActiveSAST", ErrLevel.FATAL));
                    }

                    // Bifacial Options
                    if (useBifacial)
                    {
                        itsTrackerClearance = Convert.ToDouble(ReadFarmSettings.GetInnerText("Bifacial", "GroundClearance", ErrLevel.FATAL));
                        itsTrackerPitch = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PitchSAST", ErrLevel.FATAL));
                        itsTrackerBW = Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "WActiveSAST", ErrLevel.FATAL));
                    }
                    break;

                
                case "Tilt and Roll Tracking":
                    // Tracker Parameters
                    itsTrackMode = TrackMode.SAXT;
                    itsTrackerSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AxisTiltTART", ErrLevel.FATAL));
                    itsTrackerAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AxisAzimuthTART", ErrLevel.FATAL));

                    // Operational Limits
                    itsMinRotationAngle = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "RotationMinTART", ErrLevel.FATAL));
                    itsMaxRotationAngle = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "RotationMaxTART", ErrLevel.FATAL));

                    break;
                
                case "Two Axis Tracking":
                    itsTrackMode = TrackMode.TAXT;
                    // Operational Limits
                    itsMinTilt = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MinTiltTAXT", ErrLevel.FATAL));
                    itsMaxTilt = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MaxTiltTAXT", ErrLevel.FATAL));
                    itsAzimuthRef = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AzimuthRefTAXT", ErrLevel.FATAL));
                    itsMinAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MinAzimuthTAXT", ErrLevel.FATAL));
                    itsMaxAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MaxAzimuthTAXT", ErrLevel.FATAL));
                    break;

                case "Azimuth (Vertical Axis) Tracking":
                    itsTrackMode = TrackMode.AVAT;
                    // Surface Parameters
                    SurfSlope = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "PlaneTiltAVAT", ErrLevel.FATAL));
                    // Operational Limits
                    itsAzimuthRef = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "AzimuthRefAVAT", ErrLevel.FATAL));
                    itsMinAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MinAzimuthAVAT", ErrLevel.FATAL));
                    itsMaxAzimuth = Util.DTOR * Convert.ToDouble(ReadFarmSettings.GetInnerText("O&S", "MaxAzimuthAVAT", ErrLevel.FATAL));
                    break;

                default:
                    ErrorLogger.Log("No orientation and shading was specified by the user.", ErrLevel.FATAL);
                    break;
            }

        }
    }
}
