﻿//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Export.Toolkit.IFC4;
using Revit.IFC.Export.Utility;

namespace Revit.IFC.Export.Exporter.PropertySet.Calculators
{
   /// <summary>
   /// A calculation class to calculate number of riser parameters.
   /// </summary>
   class NumberOfRiserCalculator : PropertyCalculator
   {
      /// <summary>
      /// An int variable to keep the calculated NumberOfRisers value.
      /// </summary>
      private int m_NumberOfRisers = 0;

      /// <summary>
      /// The NumberOfRiserCalculator instance.
      /// </summary>
      public static NumberOfRiserCalculator Instance { get; } = new NumberOfRiserCalculator();
      
      /// <summary>
      /// Calculates number of risers for a stair.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="extrusionCreationData">The IFCExtrusionCreationData.</param>
      /// <param name="element">The element to calculate the value.</param>
      /// <param name="elementType">The element type.</param>
      /// <returns>True if the operation succeed, false otherwise.</returns>
      public override bool Calculate(ExporterIFC exporterIFC, IFCExtrusionCreationData extrusionCreationData, Element element, ElementType elementType, EntryMap entryMap)
      {
         bool valid = true;
         if (StairsExporter.IsLegacyStairs(element))
         {
            double riserHeight, treadLength, treadLengthAtInnerSide, nosingLength, waistThickness = 0;
            int numberOfTreads;

            ExporterIFCUtils.GetLegacyStairsProperties(exporterIFC, element,
                  out m_NumberOfRisers, out numberOfTreads,
                  out riserHeight, out treadLength, out treadLengthAtInnerSide,
                  out nosingLength, out waistThickness);
         }
         else if (element is Stairs)
         {
            Stairs stairs = element as Stairs;
            m_NumberOfRisers = stairs.ActualRisersNumber;
         }
         else if (element is StairsRun)
         {
            StairsRun stairsRun = element as StairsRun;
            StairsRunType stairsRunType = stairsRun.Document.GetElement(stairsRun.GetTypeId()) as StairsRunType;
            m_NumberOfRisers = stairsRun.ActualRisersNumber;
         }
         else
         {
            valid = false;
         }

         // Get override from parameter
         int? noRiserOverride = ParameterUtil.GetIntValueFromElementOrSymbol(element, entryMap.RevitParameterName);
         if (!noRiserOverride.HasValue)
            noRiserOverride = ParameterUtil.GetIntValueFromElementOrSymbol(element, entryMap.CompatibleRevitParameterName);

         if (noRiserOverride.HasValue)
         {
            m_NumberOfRisers = noRiserOverride.Value;
            valid = true;
         }

         return valid;
      }

      /// <summary>
      /// Gets the calculated int value.
      /// </summary>
      /// <returns>
      /// The int value.
      /// </returns>
      public override int GetIntValue()
      {
         return m_NumberOfRisers;
      }
   }
}
