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
using Autodesk.Revit;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Structure;
using Revit.IFC.Export.Exporter;
using Revit.IFC.Common.Utility;
using Revit.IFC.Common.Enums;
using Revit.IFC.Export.Toolkit;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// Provides static methods for filtering elements.
   /// </summary>
   class ElementFilteringUtil
   {
      /// <summary>
      /// Gets spatial element filter.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <returns>The element filter.</returns>
      public static ElementFilter GetSpatialElementFilter(Document document, ExporterIFC exporterIFC)
      {
         return GetExportFilter(document, exporterIFC, true);
      }

      /// <summary>
      /// Gets filter for non spatial elements.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <returns>The Element filter.</returns>
      public static ElementFilter GetNonSpatialElementFilter(Document document, ExporterIFC exporterIFC)
      {
         return GetExportFilter(document, exporterIFC, false);
      }

      /// <summary>
      /// Gets element filter for export.
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="forSpatialElements">True to get spatial element filter, false for non spatial elements filter.</param>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetExportFilter(Document document, ExporterIFC exporterIFC, bool forSpatialElements)
      {
         List<ElementFilter> filters = new List<ElementFilter>();

         // Class types & categories
         ElementFilter classFilter = GetClassFilter(forSpatialElements);

         // Special handling for family instances and view specific elements
         if (!forSpatialElements)
         {
            ElementFilter familyInstanceFilter = GetFamilyInstanceFilter(exporterIFC);

            List<ElementFilter> classFilters = new List<ElementFilter>();
            classFilters.Add(classFilter);
            classFilters.Add(familyInstanceFilter);

            if (ExporterCacheManager.ExportOptionsCache.ExportAnnotations)
            {
               ElementFilter ownerViewFilter = GetViewSpecificTypesFilter(exporterIFC);
               classFilters.Add(ownerViewFilter);
            }

            classFilter = new LogicalOrFilter(classFilters);
         }

         filters.Add(classFilter);

         // Design options
         filters.Add(GetDesignOptionFilter());

         // Phases: only for non-spatial elements.  For spatial elements, we will do a check afterwards.
         if (!forSpatialElements && !ExporterCacheManager.ExportOptionsCache.ExportingLink)
            filters.Add(GetPhaseStatusFilter(document));

         return new LogicalAndFilter(filters);
      }

      /// <summary>
      /// Gets element filter for family instance.
      /// </summary>
      /// <param name="exporter">The ExporterIFC object.</param>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetFamilyInstanceFilter(ExporterIFC exporter)
      {
         List<ElementFilter> filters = new List<ElementFilter>();
         filters.Add(new ElementOwnerViewFilter(ElementId.InvalidElementId));
         filters.Add(new ElementClassFilter(typeof(FamilyInstance)));
         LogicalAndFilter andFilter = new LogicalAndFilter(filters);

         return andFilter;
      }

      /// <summary>
      /// Gets element filter meeting design option requirements.
      /// </summary>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetDesignOptionFilter()
      {
         // We will respect the active design option if we are exporting a specific view.
         ElementFilter noDesignOptionFilter = new ElementDesignOptionFilter(ElementId.InvalidElementId);
         ElementFilter primaryOptionsFilter = new PrimaryDesignOptionMemberFilter();
         ElementFilter designOptionFilter = new LogicalOrFilter(noDesignOptionFilter, primaryOptionsFilter);

         View filterView = ExporterCacheManager.ExportOptionsCache.FilterViewForExport;
         if (filterView != null)
         {
            ElementId designOptionId = DesignOption.GetActiveDesignOptionId(ExporterCacheManager.Document);
            if (designOptionId != ElementId.InvalidElementId)
            {
               ElementFilter activeDesignOptionFilter = new ElementDesignOptionFilter(designOptionId);
               return new LogicalOrFilter(designOptionFilter, activeDesignOptionFilter);
            }
         }

         return designOptionFilter;
      }

      /// <summary>
      /// Checks if element in certain category should be exported.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="element">The element.</param>
      /// <param name="allowSeparateOpeningExport">True if IfcOpeningElement is allowed to be exported.</param>
      /// <returns>True if the element should be exported, false otherwise.</returns>
      private static bool ShouldCategoryBeExported(ExporterIFC exporterIFC, Element element, bool allowSeparateOpeningExport)
      {
         IFCExportInfoPair exportType = new IFCExportInfoPair();
         ElementId categoryId;
         string ifcClassName = ExporterUtil.GetIFCClassNameFromExportTable(exporterIFC, element, out categoryId);
         if (string.IsNullOrEmpty(ifcClassName))
         {
            // Special case: these elements aren't contained in the default export layers mapping table.
            // This allows these elements to be exported by default.
            if (element is AreaScheme || element is Group)
               ifcClassName = "IfcGroup";
            else if (element is ElectricalSystem)
               ifcClassName = "IfcSystem";
            else if (element is Grid)
               ifcClassName = "IfcGrid";     // In the German template somehow the Grid does not show up in the mapping table
            else
               return false;
         }

         bool foundName = string.Compare(ifcClassName, "Default", true) != 0;
         if (foundName)
            exportType = GetExportTypeFromClassName(ifcClassName);
         if (!foundName)
            return true;

         if (exportType.ExportInstance == IFCEntityType.UnKnown)
            return false;

         // We don't export openings directly, only via the element they are opening, unless flag is set.
         if (exportType.ExportInstance == IFCEntityType.IfcOpeningElement && !allowSeparateOpeningExport)
            return false;

         // Check whether the intended Entity type is inside the export exclusion set
         IFCEntityType elementClassTypeEnum;
         if (Enum.TryParse(exportType.ExportInstance.ToString(), out elementClassTypeEnum)
            || Enum.TryParse(exportType.ExportType.ToString(), out elementClassTypeEnum))
            if (ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(elementClassTypeEnum))
               return false;

         return true;
      }

      /// <summary>
      /// Checks if an element should be exported based on parameter settings.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <param name="elementType">The element type, if any.</param>
      /// <returns>An IFCExportElement value, calculated from several parameters, or null if inconclusive.</returns>
      /// <remarks>This routine will never return IFCExportElement.ByType: it will return Yes, No, or null.</remarks>
      public static IFCExportElement? GetExportElementState(Element element, Element elementType)
      {
         Parameter exportElement = element.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT);
         IFCExportElement value = (exportElement != null) ? (IFCExportElement)exportElement.AsInteger() : IFCExportElement.ByType;
         if (value != IFCExportElement.ByType)
            return value;
         
         // Element is ByType - look at the ElementType, if it exists.
         Parameter exportElementType = elementType?.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE);
         IFCExportElementType typeValue = (exportElementType != null) ? (IFCExportElementType)exportElementType.AsInteger() : IFCExportElementType.Default;
         switch (typeValue)
         {
            case IFCExportElementType.No:
               return IFCExportElement.No;
            case IFCExportElementType.Yes:
               return IFCExportElement.Yes;
            case IFCExportElementType.Default:
               return null;
         }

         return null;
      }

      /// <summary>
      /// Checks if element should be exported using a variety of different checks.
      /// </summary>
      /// <param name="exporterIFC">The ExporterIFC object.</param>
      /// <param name="element">The element.</param>
      /// <param name="allowSeparateOpeningExport">True if IfcOpeningElement is allowed to be exported.</param>
      /// <returns>True if the element should be exported, false otherwise.</returns>
      /// <remarks>There are some inefficiencies here, as we call GetExportInfoFromParameters
      /// in other contexts.  We should attempt to get the value only once.</remarks>
      public static bool ShouldElementBeExported(ExporterIFC exporterIFC, Element element, bool allowSeparateOpeningExport)
      {
         // Allow the ExporterStateManager to say that an element should be exported regardless of settings.
         if (ExporterStateManager.CanExportElementOverride())
            return true;

         // First, check if the element is set explicitly to be exported or not exported.  This
         // overrides category settings.
         Element elementType = element.Document.GetElement(element.GetTypeId());
         IFCExportElement? exportElementState = GetExportElementState(element, elementType);
         if (exportElementState.HasValue)
             return exportElementState.Value == IFCExportElement.Yes;

         // Check to see if the category should be exported if parameters aren't set.
         // Note that in previous versions, the category override the parameter settings.  This is
         // no longer true.
         if (!ShouldCategoryBeExported(exporterIFC, element, allowSeparateOpeningExport))
            return false;

         // Check whether the intended Entity type is inside the export exclusion set
         IFCExportInfoPair exportInfo = ExporterUtil.GetIFCExportElementParameterInfo(element, IFCEntityType.IfcRoot);
         return !ExporterCacheManager.ExportOptionsCache.IsElementInExcludeList(exportInfo.ExportInstance);
      }

      /// <summary>
      /// Determines if the selected element meets extra criteria for export.
      /// </summary>
      /// <param name="exporterIFC">The exporter class.</param>
      /// <param name="element">The current element to export.</param>
      /// <param name="allowSeparateOpeningExport">True if IfcOpeningElement is allowed to be exported.</param>
      /// <returns>True if the element should be exported, false otherwise.</returns>
      public static bool CanExportElement(ExporterIFC exporterIFC, Autodesk.Revit.DB.Element element, bool allowSeparateOpeningExport)
      {
         if (!ElementFilteringUtil.ShouldElementBeExported(exporterIFC, element, allowSeparateOpeningExport))
            return false;

         // if we allow exporting parts as independent building elements, then prevent also exporting the host elements containing the parts.
         bool checkIfExportingPart = ExporterCacheManager.ExportOptionsCache.ExportPartsAsBuildingElements || element is Part;
         if (checkIfExportingPart && PartExporter.CanExportParts(element))
            return false;

         return true;
      }

      /// <summary>
      /// Checks if name is equal to base or its type name.
      /// </summary>
      /// <param name="name">The object type name.</param>
      /// <param name="baseName">The IFC base name.</param>
      /// <returns>True if equal, false otherwise.</returns>
      private static bool IsEqualToTypeName(String name, String baseName)
      {
         if (String.Compare(name, baseName, true) == 0)
            return true;

         String typeName = baseName + "Type";
         return (String.Compare(name, typeName, true) == 0);
      }

      /// <summary>
      /// Compares two strings, ignoring spaces, punctuation and case.
      /// </summary>
      /// <param name="name">The string to compare.</param>
      /// <param name="baseNameAllCapsNoSpaces">String to compare to, all caps, no punctuation or cases.</param>
      /// <returns></returns>
      private static bool CompareAlphaOnly(String name, String baseNameAllCapsNoSpaces)
      {
         if (string.IsNullOrEmpty(name))
            return string.IsNullOrEmpty(baseNameAllCapsNoSpaces);
         string nameToUpper = name.ToUpper();
         int loc = 0;
         int maxLen = baseNameAllCapsNoSpaces.Length;
         foreach (char c in nameToUpper)
         {
            if (c >= 'A' && c <= 'Z')
            {
               if (baseNameAllCapsNoSpaces[loc] != c)
                  return false;
               loc++;
               if (loc == maxLen)
                  return true;
            }
         }
         return false;
      }

      static IDictionary<string, IFCEntityType> PreIFC4Remap = new Dictionary<string, IFCEntityType>()
      {
         { "IFCAUDIOVISUALAPPLIANCE", IFCEntityType.IfcElectricApplianceType },
         { "IFCBURNER", IFCEntityType.IfcGasTerminalType },
         { "IFCELECTRICDISTRIBUTIONBOARD", IFCEntityType.IfcElectricDistributionPoint }
      };

      static IDictionary<string, IFCEntityType> IFC4Remap = new Dictionary<string, IFCEntityType>()
      {
         { "IFCGASTERMINAL", IFCEntityType.IfcBurnerType },
         { "IFCELECTRICDISTRIBUTIONPOINT", IFCEntityType.IfcElectricDistributionBoardType },
         { "IFCELECTRICHEATER", IFCEntityType.IfcSpaceHeaterType }
      };

      /// <summary>
      /// Gets export type from IFC class name.
      /// </summary>
      /// <param name="originalIFCClassName">The IFC class name.</param>
      /// <returns>The export type.</returns>
      public static IFCExportInfoPair GetExportTypeFromClassName(String originalIFCClassName)
      {
         IFCExportInfoPair exportInfoPair = new IFCExportInfoPair();

         string cleanIFCClassName = originalIFCClassName.Trim().ToUpper();
         if (cleanIFCClassName.StartsWith("IFC"))
         {
            // Here we try to catch any possible types that are missing above by checking both the class name or the type name
            // Unless there is any special treatment needed most of the above check can be done here
            string clName = cleanIFCClassName.Substring(cleanIFCClassName.Length - 4, 4).Equals("Type", StringComparison.CurrentCultureIgnoreCase) ?
               cleanIFCClassName.Substring(0, cleanIFCClassName.Length - 4) :
               cleanIFCClassName;

            // Deal with small number of IFC2x3/IFC4 types that have changed in a hardwired way.
            if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
            {
               if (PreIFC4Remap.TryGetValue(clName, out IFCEntityType ifcEntityType))
                  exportInfoPair.SetValueWithPair(ifcEntityType);
               else
                  exportInfoPair.SetValueWithPair(clName);
            }
            else
            {
               if (IFC4Remap.TryGetValue(clName, out IFCEntityType ifcEntityType))
                  exportInfoPair.SetValueWithPair(ifcEntityType);
               else
                  exportInfoPair.SetValueWithPair(clName);
            }

            if (exportInfoPair.ExportInstance == IFCEntityType.UnKnown)
               exportInfoPair.SetValueWithPair(IFCEntityType.IfcBuildingElementProxy);
         }

         exportInfoPair.ValidatedPredefinedType = IFCValidateEntry.GetValidIFCPredefinedType("NOTDEFINED", exportInfoPair.ExportType.ToString());

         return exportInfoPair;
      }

      static IDictionary<ElementId, IFCExportInfoPair> s_CategoryToExportType = null;

      static void InitializeCategoryToExportType()
      {
         if (s_CategoryToExportType != null)
            return;

         s_CategoryToExportType = new Dictionary<ElementId, IFCExportInfoPair>() {
            { new ElementId(BuiltInCategory.OST_Cornices), new IFCExportInfoPair(IFCEntityType.IfcBeam, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Ceilings), new IFCExportInfoPair(IFCEntityType.IfcCovering, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_CurtainWallPanels), new IFCExportInfoPair(IFCEntityType.IfcPlate, "CURTAIN_PANEL") },
            { new ElementId(BuiltInCategory.OST_Furniture), new IFCExportInfoPair(IFCEntityType.IfcFurniture, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Floors), new IFCExportInfoPair(IFCEntityType.IfcSlab, "FLOOR") },
            { new ElementId(BuiltInCategory.OST_IOSModelGroups), new IFCExportInfoPair(IFCEntityType.IfcGroup, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Mass), new IFCExportInfoPair(IFCEntityType.IfcBuildingElementProxy, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_CurtainWallMullions), new IFCExportInfoPair(IFCEntityType.IfcMember, "MULLION") },
            { new ElementId(BuiltInCategory.OST_Railings), new IFCExportInfoPair(IFCEntityType.IfcRailing, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Ramps), new IFCExportInfoPair(IFCEntityType.IfcRamp, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Roofs), new IFCExportInfoPair(IFCEntityType.IfcRoof, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Site), new IFCExportInfoPair(IFCEntityType.IfcSite, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Stairs), new IFCExportInfoPair(IFCEntityType.IfcStair, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Walls), new IFCExportInfoPair(IFCEntityType.IfcWall, "NOTDEFINED") },
            { new ElementId(BuiltInCategory.OST_Windows), new IFCExportInfoPair(IFCEntityType.IfcWindow, "NOTDEFINED") }
         };
      }

      /// <summary>
      /// Gets export type from category id.
      /// </summary>
      /// <param name="categoryId">The category id.</param>
      /// <returns>The export type.</returns>
      public static IFCExportInfoPair GetExportTypeFromCategoryId(ElementId categoryId)
      {
         InitializeCategoryToExportType();
         IFCExportInfoPair exportInfoPair;
         if (s_CategoryToExportType.TryGetValue(categoryId, out exportInfoPair))
            return exportInfoPair;
         return new IFCExportInfoPair();
      }

      /// <summary>
      /// Gets element filter for specific views.
      /// </summary>
      /// <param name="exporter">The ExporterIFC object.</param>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetViewSpecificTypesFilter(ExporterIFC exporter)
      {
         ElementFilter ownerViewFilter = GetOwnerViewFilter(exporter);

         List<Type> viewSpecificTypes = new List<Type>();
         viewSpecificTypes.Add(typeof(TextNote));
         viewSpecificTypes.Add(typeof(FilledRegion));
         ElementMulticlassFilter classFilter = new ElementMulticlassFilter(viewSpecificTypes);


         LogicalAndFilter viewSpecificTypesFilter = new LogicalAndFilter(ownerViewFilter, classFilter);
         return viewSpecificTypesFilter;
      }

      /// <summary>
      /// Gets element filter to match elements which are owned by a particular view.
      /// </summary>
      /// <param name="exporter">The exporter.</param>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetOwnerViewFilter(ExporterIFC exporter)
      {
         List<ElementFilter> filters = new List<ElementFilter>();
         ICollection<ElementId> viewIds = ExporterCacheManager.DBViewsToExport.Keys;
         foreach (ElementId id in viewIds)
         {
            filters.Add(new ElementOwnerViewFilter(id));
         }
         filters.Add(new ElementOwnerViewFilter(ElementId.InvalidElementId));
         LogicalOrFilter viewFilters = new LogicalOrFilter(filters);

         return viewFilters;
      }

      /// <summary>
      /// Gets element filter that match certain types.
      /// </summary>
      /// <param name="forSpatialElements">True if to get filter for spatial element, false for other elements.</param>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetClassFilter(bool forSpatialElements)
      {
         if (forSpatialElements)
         {
            return new ElementClassFilter(typeof(SpatialElement));
         }
         else
         {
            List<Type> excludedTypes = new List<Type>();

            // FamilyInstances are handled in separate filter.
            excludedTypes.Add(typeof(FamilyInstance));

            // Spatial element are exported in a separate pass.
            excludedTypes.Add(typeof(SpatialElement));

            // AreaScheme elements are exported as groups after all Areas have been exported.
            excludedTypes.Add(typeof(AreaScheme));
            // FabricArea elements are exported as groups after all FabricSheets have been exported.
            excludedTypes.Add(typeof(FabricArea));

            if (!ExporterCacheManager.ExportOptionsCache.ExportAnnotations)
               excludedTypes.Add(typeof(CurveElement));

            excludedTypes.Add(typeof(ElementType));

            excludedTypes.Add(typeof(BaseArray));

            excludedTypes.Add(typeof(FillPatternElement));
            excludedTypes.Add(typeof(LinePatternElement));
            excludedTypes.Add(typeof(Material));
            excludedTypes.Add(typeof(GraphicsStyle));
            excludedTypes.Add(typeof(Family));
            excludedTypes.Add(typeof(SketchPlane));
            excludedTypes.Add(typeof(View));
            excludedTypes.Add(typeof(Autodesk.Revit.DB.Structure.LoadBase));

            // curtain wall sub-types we are ignoring.
            excludedTypes.Add(typeof(CurtainGridLine));
            // excludedTypes.Add(typeof(Mullion));

            // this will be gotten from the element(s) it cuts.
            excludedTypes.Add(typeof(Opening));

            // 2D types we are ignoring
            excludedTypes.Add(typeof(SketchBase));
            excludedTypes.Add(typeof(FaceSplitter));

            // 2D types covered by the element owner view filter
            excludedTypes.Add(typeof(TextNote));
            excludedTypes.Add(typeof(FilledRegion));

            // exclude levels that are covered in BeginExport
            excludedTypes.Add(typeof(Level));

            // exclude analytical models
            excludedTypes.Add(typeof(Autodesk.Revit.DB.Structure.AnalyticalElement));
            ElementFilter excludedClassFilter = new ElementMulticlassFilter(excludedTypes, true);

            List<BuiltInCategory> excludedCategories = new List<BuiltInCategory>();

            // Native Revit types without match in API
            excludedCategories.Add(BuiltInCategory.OST_ConduitCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_ConduitFittingCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_DecalElement);
            //excludedCategories.Add(BuiltInCategory.OST_Parts);
            //excludedCategories.Add(BuiltInCategory.OST_RvtLinks);
            excludedCategories.Add(BuiltInCategory.OST_DuctCurvesCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_DuctFittingCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_FlexDuctCurvesCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_FlexPipeCurvesCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_IOS_GeoLocations);
            excludedCategories.Add(BuiltInCategory.OST_PipeCurvesCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_PipeFittingCenterLine);
            excludedCategories.Add(BuiltInCategory.OST_Property);
            excludedCategories.Add(BuiltInCategory.OST_SiteProperty);
            excludedCategories.Add(BuiltInCategory.OST_SitePropertyLineSegment);
            excludedCategories.Add(BuiltInCategory.OST_TopographyContours);
            excludedCategories.Add(BuiltInCategory.OST_Viewports);
            excludedCategories.Add(BuiltInCategory.OST_Views);

            // Exclude elements with no category. 
            excludedCategories.Add(BuiltInCategory.INVALID);

            ElementMulticategoryFilter excludedCategoryFilter = new ElementMulticategoryFilter(excludedCategories, true);

            LogicalAndFilter exclusionFilter = new LogicalAndFilter(excludedClassFilter, excludedCategoryFilter);

            ElementOwnerViewFilter ownerViewFilter = new ElementOwnerViewFilter(ElementId.InvalidElementId);

            LogicalAndFilter returnedFilter = new LogicalAndFilter(exclusionFilter, ownerViewFilter);

            return returnedFilter;
         }
      }

      /// <summary>
      /// Checks if the room is in an invalid phase.
      /// </summary>
      /// <param name="element">The element, which may or may not be a room element.</param>
      /// <returns>True if the element is in the room, has a phase set, which is different from the active phase.</returns>
      public static bool IsRoomInInvalidPhase(Element element)
      {
         if (element is Room)
         {
            Parameter phaseParameter = element.get_Parameter(BuiltInParameter.ROOM_PHASE);
            if (phaseParameter != null)
            {
               ElementId phaseId = phaseParameter.AsElementId();
               if (phaseId != ElementId.InvalidElementId && phaseId != ExporterCacheManager.ExportOptionsCache.ActivePhaseId)
                  return true;
            }
         }

         return false;
      }

      /// <summary>
      /// Gets element filter that match certain phases. 
      /// </summary>
      /// <param name="document">The Revit document.</param>
      /// <returns>The element filter.</returns>
      private static ElementFilter GetPhaseStatusFilter(Document document)
      {
         ElementId phaseId = ExporterCacheManager.ExportOptionsCache.ActivePhaseId;

         List<ElementOnPhaseStatus> phaseStatuses = new List<ElementOnPhaseStatus>();
         phaseStatuses.Add(ElementOnPhaseStatus.None);  //include "none" because we might want to export phaseless elements.
         phaseStatuses.Add(ElementOnPhaseStatus.Existing);
         phaseStatuses.Add(ElementOnPhaseStatus.New);

         return new ElementPhaseStatusFilter(phaseId, phaseStatuses);
      }

      private static IDictionary<ElementId, bool> m_CategoryVisibilityCache = new Dictionary<ElementId, bool>();

      /// <summary>
      /// Initialize the category visibility cache
      /// </summary>
      public static void InitCategoryVisibilityCache()
      {
         m_CategoryVisibilityCache.Clear();
      }

      /// <summary>
      /// Checks if a category is visible for certain view.
      /// </summary>
      /// <param name="category">The category.</param>
      /// <param name="filterView">The view.</param>
      /// <returns>True if the category is visible, false otherwise.</returns>
      public static bool IsCategoryVisible(Category category, View filterView)
      {
         // This routine is generally used to decide whether or not to export geometry assigned to a praticular category.
         // Default behavior is to return true, even for a null category.  In general, we want to err on the side of showing geometry over hiding it.
         if (category == null || filterView == null)
            return true;

         bool isVisible = false;
         if (m_CategoryVisibilityCache.TryGetValue(category.Id, out isVisible))
            return isVisible;

         // The category will be visible if either we don't allow visibility controls (default: true), or
         // we do allow visibility controls and the category is visible in the view.
         isVisible = (!category.get_AllowsVisibilityControl(filterView) || category.get_Visible(filterView));
         m_CategoryVisibilityCache[category.Id] = isVisible;
         return isVisible;
      }

      /// <summary>
      /// Checks if element is visible for certain view.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>True if the element is visible, false otherwise.</returns>
      public static bool IsElementVisible(Element element)
      {
         View filterView = ExporterCacheManager.ExportOptionsCache.FilterViewForExport;
         if (filterView == null)
            return true;

         bool hidden = element.IsHidden(filterView);
         if (hidden)
            return false;

         Category category = element.Category;
         hidden = !IsCategoryVisible(category, filterView);
         if (hidden)
            return false;

         bool temporaryVisible = filterView.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, element.Id);

         return temporaryVisible;
      }

      /// <summary>
      /// Checks if the IFC type is MEP type.
      /// </summary>
      /// <param name="exportType">IFC Export Type to check</param>
      /// <returns>True for MEP type of elements.</returns>
      public static bool IsMEPType(IFCExportInfoPair exportType)
      {
         bool instanceIsMEPInst = IfcSchemaEntityTree.IsSubTypeOf(ExporterCacheManager.ExportOptionsCache.FileVersion, exportType.ExportInstance.ToString(), IFCEntityType.IfcDistributionElement.ToString(), strict:false);

         // The Type probably is not needed for check?
         bool typeIsMEPType = IfcSchemaEntityTree.IsSubTypeOf(ExporterCacheManager.ExportOptionsCache.FileVersion, exportType.ExportType.ToString(), IFCEntityType.IfcDistributionElementType.ToString(), strict:false);

         return (instanceIsMEPInst);
      }

      /// <summary>
      /// Check if an element assigned to IfcBuildingElementProxy is of MEP Type (by checking its connectors) to enable IfcBuildingElementProxy to take part
      /// in the System component and connectivity
      /// </summary>
      /// <param name="element">The element</param>
      /// <param name="exportType">IFC Export Type to check: only for IfcBuildingElementProxy or IfcBuildingElementProxyType</param>
      /// <returns></returns>
      public static bool ProxyForMEPType(Element element, IFCExportInfoPair exportType)
      {
         if ((exportType.ExportInstance == IFCEntityType.IfcBuildingElementProxy) || (exportType.ExportType == IFCEntityType.IfcBuildingElementProxyType))
         {
            try
            {
               if (element is FamilyInstance)
               {
                  MEPModel m = ((FamilyInstance)element).MEPModel;
                  if (m != null && m.ConnectorManager != null)
                  {
                     return true;
                  }
               }
               else
                  return false;
            }
            catch
            {
            }
         }

         return false;
      }

      /// <summary>
      /// Get valid IFC entity type by using the official IFC schema (using the XML schema). It checks the non-abstract valid entity. 
      /// If it is found to be abstract, it will try to find its supertype until it finds a non-abstract type.  
      /// </summary>
      /// <param name="entityType">the IFC entity type (string) to check</param>
      /// <returns>return the appropriate IFCEntityType enumeration or Unknown</returns>
      public static IFCEntityType GetValidIFCEntityType (string entityType)
      {
         IFCVersion ifcVersion = ExporterCacheManager.ExportOptionsCache.FileVersion;
         IFCEntityType ret = IFCEntityType.UnKnown;

         var ifcEntitySchemaTree = IfcSchemaEntityTree.GetEntityDictFor(ExporterCacheManager.ExportOptionsCache.FileVersion);
         if (ifcEntitySchemaTree == null || ifcEntitySchemaTree.IfcEntityDict == null || ifcEntitySchemaTree.IfcEntityDict.Count == 0)
            throw new Exception("Unable to locate IFC Schema xsd file! Make sure the relevant xsd " + ExporterCacheManager.ExportOptionsCache.FileVersion + " exists.");

         IfcSchemaEntityNode node = ifcEntitySchemaTree.Find(entityType);
         IFCEntityType ifcType = IFCEntityType.UnKnown;
         if (node != null && !node.isAbstract)
         {
            // Only IfcProduct or IfcTypeProduct can be assigned for export type
            //if (!node.IsSubTypeOf("IfcProduct") && !node.IsSubTypeOf("IfcTypeProduct") && !node.Name.Equals("IfcGroup", StringComparison.InvariantCultureIgnoreCase))
            if ((node.IsSubTypeOf("IfcObject") && 
                     (node.IsSubTypeOf("IfcProduct") || node.IsSubTypeOf("IfcGroup") || node.Name.Equals("IfcGroup", StringComparison.InvariantCultureIgnoreCase)))
                  || node.IsSubTypeOf("IfcProject") || node.Name.Equals("IfcProject", StringComparison.InvariantCultureIgnoreCase)
                  || node.IsSubTypeOf("IfcTypeObject"))
            {
               if (IFCEntityType.TryParse(entityType, true, out ifcType))
                  ret = ifcType;
            }
            else
               ret = ifcType;
         }
         else if (node != null && node.isAbstract)
         {
            node = IfcSchemaEntityTree.FindNonAbsSuperType(ifcVersion, entityType, "IfcProduct", "IfcProductType", "IfcGroup", "IfcProject");
            if (node != null)
            {
               if (Enum.TryParse<IFCEntityType>(node.Name, true, out ifcType))
                  ret = ifcType;
            }
         }

         return ret;
      }

      /// <summary>
      /// Get valid IFC entity type by using the official IFC schema (using the XML schema). It checks the non-abstract valid entity. 
      /// If it is found to be abstract, it will try to find its supertype until it finds a non-abstract type. 
      /// </summary>
      /// <param name="entityType">the IFC Entity type enum</param>
      /// <returns>return the appropriate entity type or Unknown</returns>
      public static IFCEntityType GetValidIFCEntityType (IFCEntityType entityType)
      {
         return GetValidIFCEntityType(entityType.ToString());
      }
   }
}