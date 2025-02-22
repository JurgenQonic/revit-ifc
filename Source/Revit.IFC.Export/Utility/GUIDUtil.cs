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
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// Provides static methods for GUID related manipulations.
   /// </summary>
   public class GUIDUtil
   {
      /// <summary>
      /// An enum that contains fake element ids corresponding to the IfcProject, IfcSite, and IfcBuilding entities.
      /// </summary>
      /// <remarks>The numbers below allow for the generation of stable GUIDs for these entities, that are
      /// consistent with previous versions of the exporter.</remarks>
      public enum ProjectLevelGUIDType
      {
         Building = -15,
         Project = -16,
         Site = -14
      };

      static string s_ConversionTable_2X = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

      private static System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();

      static private string ConvertToIFCGuid(System.Guid guid)
      {
         byte[] byteArray = guid.ToByteArray();
         ulong[] num = new ulong[6];
         num[0] = byteArray[3];
         num[1] = byteArray[2] * (ulong)65536 + byteArray[1] * (ulong)256 + byteArray[0];
         num[2] = byteArray[5] * (ulong)65536 + byteArray[4] * (ulong)256 + byteArray[7];
         num[3] = byteArray[6] * (ulong)65536 + byteArray[8] * (ulong)256 + byteArray[9];
         num[4] = byteArray[10] * (ulong)65536 + byteArray[11] * (ulong)256 + byteArray[12];
         num[5] = byteArray[13] * (ulong)65536 + byteArray[14] * (ulong)256 + byteArray[15];

         char[] buf = new char[22];
         int offset = 0;

         for (int ii = 0; ii < 6; ii++)
         {
            int len = (ii == 0) ? 2 : 4;
            for (int jj = 0; jj < len; jj++)
            {
               buf[offset + len - jj - 1] = s_ConversionTable_2X[(int)(num[ii] % 64)];
               num[ii] /= 64;
            }
            offset += len;
         }

         return new string(buf);
      }

      /// <summary>
      /// Checks if a GUID string is properly formatted as an IFC GUID.
      /// </summary>
      /// <param name="guid">The GUID value to check.</param>
      /// <returns>True if it qualifies as a valid IFC GUID.</returns>
      static public bool IsValidIFCGUID(string guid)
      {
         if (guid == null)
            return false;

         if (guid.Length != 22)
            return false;

         // The first character is limited to { 0, 1, 2, 3 }.
         if (guid[0] < '0' || guid[0] > '3')
            return false;

         // Redundant check for the first character, but it's a fairly
         // inexpensive check.
         foreach (char guidChar in guid)
         {
            if ((guidChar >= '0' && guidChar <= '9') ||
                (guidChar >= 'A' && guidChar <= 'Z') ||
                (guidChar >= 'a' && guidChar <= 'z') ||
                (guidChar == '_' || guidChar == '$'))
               continue;

            return false;
         }

         return true;
      }

      /// <summary>
      /// Creates a Project, Site, or Building GUID.  If a shared parameter is set with a valid IFC GUID value,
      /// that value will override the default one.
      /// </summary>
      /// <param name="document">The document.</param>
      /// <param name="guidType">The GUID being created.</param>
      /// <returns>The IFC GUID value.</returns>
      /// <remarks>For Sites, the user should only use this routine if there is no Site element in the file.  Otherwise, they
      /// should use CreateSiteGUID below, which takes an Element pointer.</remarks>
      static public string CreateProjectLevelGUID(Document document, ProjectLevelGUIDType guidType)
      {
         string parameterName = "Ifc" + guidType.ToString() + " GUID";
         ProjectInfo projectInfo = document.ProjectInformation;

         BuiltInParameter parameterId = BuiltInParameter.INVALID;
         switch (guidType)
         {
            case ProjectLevelGUIDType.Building:
               parameterId = BuiltInParameter.IFC_BUILDING_GUID;
               break;
            case ProjectLevelGUIDType.Project:
               parameterId = BuiltInParameter.IFC_PROJECT_GUID;
               break;
            case ProjectLevelGUIDType.Site:
               parameterId = BuiltInParameter.IFC_SITE_GUID;
               break;
            default:
               // This should eventually log an error.
               return null;
         }

         if (projectInfo != null)
         {
            string paramValue = null;
            ParameterUtil.GetStringValueFromElement(projectInfo, parameterName, out paramValue);
            if (!IsValidIFCGUID(paramValue) && parameterId != BuiltInParameter.INVALID)
               ParameterUtil.GetStringValueFromElement(projectInfo, parameterId, out paramValue);

            if (IsValidIFCGUID(paramValue))
               return paramValue;
         }

         ElementId projectLevelElementId = new ElementId((int)guidType);
         Guid guid = ExportUtils.GetExportId(document, projectLevelElementId);
         string ifcGUID = ConvertToIFCGuid(guid);

         if ((projectInfo != null) && ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID)
         {

            if (parameterId != BuiltInParameter.INVALID)
               ExporterCacheManager.GUIDsToStoreCache[new KeyValuePair<ElementId, BuiltInParameter>(projectInfo.Id, parameterId)] = ifcGUID;
         }
         return ifcGUID;
      }

      /// <summary>
      /// Creates a Site GUID for a Site element.  If "IfcSite GUID" is set to a valid IFC GUID
      /// in the site element, that value will override any value stored in ProjectInformation.
      /// </summary>
      /// <param name="document">The document pointer.</param>
      /// <param name="element">The Site element.</param>
      /// <returns>The GUID as a string.</returns>
      static public string CreateSiteGUID(Document document, Element element)
      {
         if (element != null)
         {
            ParameterUtil.GetStringValueFromElement(element, "IfcSiteGUID", out string paramValue);
            if (IsValidIFCGUID(paramValue))
               return paramValue;
         }

         return CreateProjectLevelGUID(document, ProjectLevelGUIDType.Site);
      }

      /// <summary>
      /// Returns the GUID for a storey level, depending on whether we are using R2009 GUIDs or current GUIDs.
      /// </summary>
      /// <param name="level">The level.</param>
      /// <returns>The GUID.</returns>
      public static string GetLevelGUID(Level level)
      {
         if (!ExporterCacheManager.ExportOptionsCache.GUIDOptions.Use2009BuildingStoreyGUIDs)
         {
            string ifcGUID = ExporterIFCUtils.CreateAlternateGUID(level);
            if (ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID)
               ExporterCacheManager.GUIDsToStoreCache[new KeyValuePair<ElementId, BuiltInParameter>(level.Id, BuiltInParameter.IFC_GUID)] = ifcGUID;
            return ifcGUID;
         }
         else
         {
            return CreateGUID(level);
         }
      }

      /// <summary>
      /// Create a sub-element GUID for a given element, or a random GUID if element is null, or subindex is nonpositive.
      /// </summary>
      /// <param name="element">The element - null allowed.</param>
      /// <param name="subIndex">The index value - should be greater than 0.</param>
      /// <returns>The GUID.</returns>
      static public string CreateSubElementGUID(Element element, int subIndex)
      {
         if (element == null || subIndex <= 0)
            return CreateGUID();
         return ExporterIFCUtils.CreateSubElementGUID(element, subIndex);
      }

      /// <summary>
      /// Generates IFC GUID from not empty string.
      /// </summary>
      /// <param name="uniqueString">String which should uniquely identify IFC entity.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      private static string GenerateIFCGuidFrom(string uniqueString)
      {
         byte[] hash = md5.ComputeHash(Encoding.Default.GetBytes(uniqueString));
         return ConvertToIFCGuid(new Guid(hash));
      }

      /// <summary>
      /// Generates IFC GUID from an IFC entity type, an identifier and a handle.
      /// </summary>
      /// <param name="type">The IFC entity type.</param>
      /// <param name="name">The name of the object, unique to this handle.</param>
      /// <param name="handle">The primary handle.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      public static string GenerateIFCGuidFrom(IFCEntityType type, string name, IFCAnyHandle handle)
      {
         return GenerateIFCGuidFrom(type.ToString() + ":" + name + ":" + ExporterUtil.GetGlobalId(handle));
      }

      /// <summary>
      /// Generates IFC GUID from an IFC entity type and a handle.
      /// </summary>
      /// <param name="type">The IFC entity type.</param>
      /// <param name="handle">The primary handle.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      public static string GenerateIFCGuidFrom(IFCEntityType type, IFCAnyHandle handle)
      {
         return GenerateIFCGuidFrom(type.ToString() + ":" + ExporterUtil.GetGlobalId(handle));
      }

      /// <summary>
      /// Generates IFC GUID from an element and an integer.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <param name="index">The sub-element index as a string.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      public static string GenerateIFCGuidFrom(string index, Element firstElement, Element secondElement)
      {
         string firstBaseGuid = CreateSimpleGUID(firstElement);
         string secondBaseGuid = CreateSimpleGUID(secondElement);
         return GenerateIFCGuidFrom(index + firstBaseGuid + secondBaseGuid);
      }
      
      /// <summary>
      /// Generates IFC GUID from an element and an integer.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <param name="index">The sub-element index as a string.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      public static string GenerateIFCGuidFrom(Element element, string index)
      {
         string baseGuid = CreateSimpleGUID(element);
         return GenerateIFCGuidFrom(baseGuid + "Sub-element:" + index);
      }

      /// <summary>
      /// Generates IFC GUID from an IFC entity type and a collection of handles.
      /// </summary>
      /// <param name="type">The ifc entity type.</param>
      /// <param name="handle">The primary handle.</param>
      /// <param name="relatedHandles">A collection of handles related to the primary handle.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      public static string GenerateIFCGuidFrom(IFCEntityType type, IFCAnyHandle firstHandle,
         IFCAnyHandle secondHandle)
      {
         string guidString = type.ToString() + ":" + ExporterUtil.GetGlobalId(firstHandle) +
            ExporterUtil.GetGlobalId(secondHandle);
         return GenerateIFCGuidFrom(guidString);
      }

      /// <summary>
      /// Generates IFC GUID from an IFC entity type and a string unique to this project.
      /// </summary>
      /// <param name="type">The ifc entity type.</param>
      /// <param name="uniqueKey">The key unique to this project.</param>
      /// <returns>String in IFC GUID format. Uniqueness is highly likely, but not guaranteed even
      /// if input string is unique.</returns>
      public static string GenerateProjectIFCGuidFrom(IFCEntityType type, string uniqueKey)
      {
         string guidString = ExporterUtil.GetGlobalId(ExporterCacheManager.ProjectHandle) + 
            type.ToString() + ":" + uniqueKey;
         return GenerateIFCGuidFrom(guidString);
      }

      static private string CreateSimpleGUID(Element element)
      {
         Guid guid = ExportUtils.GetExportId(element.Document, element.Id);
         return ConvertToIFCGuid(guid);
      }

      static private string CreateGUIDBase(Element element, BuiltInParameter parameterName, out bool shouldStore)
      {
         string ifcGUID = null;
         shouldStore = CanStoreGUID(element);

         // Avoid getting into an object if the object is part of the Group. It may cause regrenerate that invalidate other ElementIds
         if (shouldStore && ExporterCacheManager.ExportOptionsCache.GUIDOptions.AllowGUIDParameterOverride)
            ParameterUtil.GetStringValueFromElement(element, parameterName, out ifcGUID);

         if (!IsValidIFCGUID(ifcGUID))
            ifcGUID = CreateSimpleGUID(element);

         return ifcGUID;
      }

      static private bool CanStoreGUID(Element element)
      {
         bool isCurtainElement = false;

         // Cannot set IfcGUID to curtain wall because doing so will potentially invalidate other element/delete the insert (even in interactive mode)
         if (element is Wall)
         {
            Wall wallElem = element as Wall;
            isCurtainElement = wallElem.CurtainGrid != null;
         }
         return !isCurtainElement;
      }

      /// <summary>
      /// Updates IfcGUID value
      /// </summary>
      /// <param name="element">The element.</param>
      /// <param name="guid">New GUID for the element.</param>
      static public void UpdateIFCGUIDValue(Element element, string guid)
      {
         if ((element != null) && CanStoreGUID(element) && ExporterCacheManager.ExportOptionsCache.GUIDOptions.AllowGUIDParameterOverride)
         {
            BuiltInParameter parameterName = (element is ElementType) ? BuiltInParameter.IFC_TYPE_GUID : BuiltInParameter.IFC_GUID;
            ExporterCacheManager.GUIDsToStoreCache[new KeyValuePair<ElementId, BuiltInParameter>(element.Id, parameterName)] = guid;
         }
      }

      public static string RegisterGUID(Element element, string guid)
      {
         // We want to make sure that we don't write out duplicate GUIDs to the file.  As such, we will check the GUID against
         // already created guids, and export a random GUID if necessary.
         // TODO: log message to user.
         if (ExporterCacheManager.GUIDCache.Contains(guid))
         {
            guid = CreateGUID();
            UpdateIFCGUIDValue(element, guid);
         }
         else
            ExporterCacheManager.GUIDCache.Add(guid);

         return guid;
      }

      /// <summary>
      /// Thin wrapper for the CreateGUID() Revit API function.
      /// </summary>
      /// <returns>A random GUID.</returns>
      static private string CreateGUID()
      {
         return ExporterIFCUtils.CreateGUID();
      }

      /// <summary>
      /// Thin wrapper for the CreateGUID(element) Revit API function.
      /// </summary>
      /// <param name="element">The element.</param>
      /// <returns>A consistent GUID for the element.</returns>
      static public string CreateGUID(Element element)
      {
         if (element == null)
            return CreateGUID();

         bool shouldStore;
         BuiltInParameter parameterName = (element is ElementType) ? BuiltInParameter.IFC_TYPE_GUID : BuiltInParameter.IFC_GUID;

         string ifcGUID = CreateGUIDBase(element, parameterName, out shouldStore);
         if (shouldStore && ExporterCacheManager.ExportOptionsCache.GUIDOptions.StoreIFCGUID ||
             (ExporterCacheManager.ExportOptionsCache.GUIDOptions.Use2009BuildingStoreyGUIDs && element is Level))
            ExporterCacheManager.GUIDsToStoreCache[new KeyValuePair<ElementId, BuiltInParameter>(element.Id, parameterName)] = ifcGUID;

         return ifcGUID;
      }

      /// <summary>
      /// Returns true if elementGUID == CreateGUID(element).
      /// </summary>
      /// <param name="element">The element.</param>
      /// <param name="elementGUID">The GUID to check</param>
      /// <returns>True if elementGUID == CreateGUID(element)</returns>
      static public bool IsGUIDFor(Element element, string elementGUID)
      {
         BuiltInParameter parameterName = (element is ElementType) ? BuiltInParameter.IFC_TYPE_GUID : BuiltInParameter.IFC_GUID;

         return (string.Compare(elementGUID, CreateGUIDBase(element, parameterName, out _)) == 0);
      }
   }
}