using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LinkeD365.FlowToVisio
{
    /// <summary>
    /// The Visio package handling of FlowToVisioControl (VisioGen.cs and VisionGen.Base.cs) without the WinForms
    /// control: a copy of the template, one page per flow, and the template page removed at the end.
    /// </summary>
    public sealed class VisioDocument : IDisposable
    {
        private const string RelDocument = "http://schemas.microsoft.com/visio/2010/relationships/document";
        private const string RelPages = "http://schemas.microsoft.com/visio/2010/relationships/pages";
        private const string RelPage = "http://schemas.microsoft.com/visio/2010/relationships/page";

        private readonly Package package;
        private readonly PackagePart pages;
        private readonly PackagePart page;
        private int flowCount;

        public VisioDocument(string fileName)
        {
            using (var template = Assembly.GetExecutingAssembly().GetManifestResourceStream("VisioTemplate.vsdx"))
            using (var file = File.Create(fileName))
                template.CopyTo(file);

            package = Package.Open(fileName, FileMode.Open, FileAccess.ReadWrite);
            var document = GetPackagePart(package, RelDocument);
            pages = GetPackagePart(package, document, RelPages);
            page = GetPackagePart(package, pages, RelPage);
        }

        /// <summary>Draws one flow on a new page. Returns the number of shapes drawn.</summary>
        public int AddFlow(JObject flowObject, string pageName)
        {
            Utils.XMLPage = GetXMLFromPart(page);
            Utils.Root = flowObject;
            Utils.actionCount = 0;
            Connection.SetAPIs(flowObject);

            var triggerProperty = flowObject["properties"]["definition"]["triggers"].First() as JProperty;
            var triggerShape = Utils.AddAction(triggerProperty, null, 0, 1);
            Utils.AddComment(triggerShape);
            var first = flowObject["properties"]["definition"]["actions"].Children<JProperty>().Where(a => !Utils.HasRunAfter(a.Value));
            if (first.Any())
                Utils.AddActions(first, triggerShape);

            foreach (var shapeName in Utils.VisioTemplates)
                triggerShape.GetTemplateShape(shapeName)?.Remove();

            flowCount++;
            CreateNewPage(Utils.XMLPage, new Uri($"/visio/pages/flowPage{flowCount}.xml", UriKind.Relative), page.ContentType, pageName);
            var shapes = Utils.actionCount;
            Utils.actionCount = 0;
            return shapes;
        }

        public void Dispose()
        {
            RemoveTemplate();
            RecalcDocument();
            package.Close();
        }

        private void CreateNewPage(XDocument partXML, Uri packageLocation, string contentType, string name)
        {
            if (!package.PartExists(packageLocation))
            {
                var newPackagePart = package.CreatePart(packageLocation, contentType);
                using (var partStream = newPackagePart.GetStream(FileMode.Create, FileAccess.ReadWrite))
                    partXML.Save(partStream);
            }

            var rel = pages.CreateRelationship(packageLocation, TargetMode.Internal, RelPage);
            var pagesXml = GetXMLFromPart(pages);
            var templatePage = pagesXml.Root.Elements().First(ele => ele.Attribute("NameU").Value == "Page-1");
            var pageXML = new XElement(templatePage);
            pageXML.SetAttributeValue("Name", name);
            pageXML.SetAttributeValue("NameU", name);
            pageXML.SetAttributeValue("IsCustomNameU", "1");
            pageXML.SetAttributeValue("IsCustomName", "1");
            pageXML.SetAttributeValue("ID", templatePage.Parent.Elements().Count());
            XNamespace ns = pagesXml.Root.GetNamespaceOfPrefix("r");
            pageXML.Elements().First(el => el.Name.LocalName == "Rel").SetAttributeValue(ns + "id", rel.Id);
            templatePage.Parent.Add(new XElement(pageXML));
            SaveXDocumentToPart(pages, pagesXml);
        }

        private void RemoveTemplate()
        {
            package.DeletePart(page.Uri);

            var pagesXML = GetXMLFromPart(pages);
            pagesXML.Root.Elements().First(ele => ele.Attribute("NameU").Value == "Page-1").Remove();

            var pagesURI = pages.Uri;
            var contentType = pages.ContentType;
            var relationships = pages.GetRelationships().Where(rel => rel.Id != "rId1").ToList();
            package.DeletePart(pages.Uri);
            var newPages = package.CreatePart(pagesURI, contentType);
            foreach (var relationship in relationships)
                newPages.CreateRelationship(relationship.TargetUri, TargetMode.Internal, relationship.RelationshipType, relationship.Id);
            SaveXDocumentToPart(newPages, pagesXML);
        }

        /// <summary>Asks Visio to recalculate on open, so connectors and containers settle.</summary>
        private void RecalcDocument()
        {
            var customPart = GetPackagePart(package, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties");
            var customPartXML = GetXMLFromPart(customPart);
            var props = customPartXML.Descendants().Where(el => el.Name.LocalName == "property").ToList();
            if (props.Any(p => p.Attribute("name")?.Value == "RecalcDocument"))
                return;

            var ids = props.Select(p => p.Attribute("pid").Value).ToList();
            var pid = 2;
            while (ids.Contains(pid.ToString())) pid++;
            var root = customPartXML.Elements().First();
            XNamespace vt = root.GetNamespaceOfPrefix("vt");
            XNamespace schema = root.GetDefaultNamespace();
            root.Add(new XElement(schema + "property",
                new XAttribute("pid", pid.ToString()),
                new XAttribute("name", "RecalcDocument"),
                new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                new XElement(vt + "bool", "true")));
            SaveXDocumentToPart(customPart, customPartXML);
        }

        private static void SaveXDocumentToPart(PackagePart packagePart, XDocument partXML)
        {
            var settings = new XmlWriterSettings { Encoding = Encoding.UTF8 };
            using (var stream = packagePart.GetStream(FileMode.Create, FileAccess.Write))
            using (var writer = XmlWriter.Create(stream, settings))
                partXML.WriteTo(writer);
        }

        private static PackagePart GetPackagePart(Package filePackage, string relationship)
        {
            var packageRel = filePackage.GetRelationshipsByType(relationship).FirstOrDefault();
            if (packageRel == null) return null;
            var docUri = PackUriHelper.ResolvePartUri(new Uri("/", UriKind.Relative), packageRel.TargetUri);
            return filePackage.GetPart(docUri);
        }

        private static PackagePart GetPackagePart(Package filePackage, PackagePart sourcePart, string relationship)
        {
            var packageRel = sourcePart.GetRelationshipsByType(relationship).FirstOrDefault();
            if (packageRel == null) return null;
            return filePackage.GetPart(PackUriHelper.ResolvePartUri(sourcePart.Uri, packageRel.TargetUri));
        }

        private static XDocument GetXMLFromPart(PackagePart packagePart)
        {
            using (var partStream = packagePart.GetStream(FileMode.Open, FileAccess.Read))
                return XDocument.Load(partStream, LoadOptions.PreserveWhitespace);
        }
    }
}
