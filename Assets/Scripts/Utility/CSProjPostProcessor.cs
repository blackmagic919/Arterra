using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.IO;
using UnityEditor;

public class CsprojPostProcessor : AssetPostprocessor
{
    private const string XmlDocumentationTag = "<GenerateDocumentationFile>true</GenerateDocumentationFile> \n  <DocumentationFile>./Docs/Arterra.xml</DocumentationFile>\n";

    static void OnGeneratedCSProjectFiles()
    {
        string[] csprojFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*Assembly-CSharp.csproj");
        
        foreach (string csprojFile in csprojFiles)
        {
            string content = File.ReadAllText(csprojFile);

            // Check if XML documentation generation is already enabled
            if (content.Contains(XmlDocumentationTag)) continue;
            int propertyGroupIndex = content.IndexOf("<PropertyGroup>");
            if (propertyGroupIndex == -1) continue; //No PropertyGroup found
            
            int endOfPropertyGroup = content.IndexOf("</PropertyGroup>", propertyGroupIndex);
            if (endOfPropertyGroup != -1)
            {
                content = content.Insert(endOfPropertyGroup, $"\n    {XmlDocumentationTag}");
                File.WriteAllText(csprojFile, content);
            }
        }
    }
}