﻿using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using Microsoft.WindowsAPICodePack.Dialogs;
using MoreLinq;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Input;
using dbg = Shared.Dbg;
using fi = Shared.Filter;
using lad = MEPUtils.CreateInstrumentation.ListsAndDicts;
using mp = Shared.MepUtils;
using tr = Shared.Transformation;
using Autodesk.Revit.Attributes;
using System.Diagnostics;
using Autodesk.Revit.DB.Architecture;
using sl = Shared.SimpleLogger;
using System.Security.Cryptography;

namespace MEPUtils.WriteRoomNumbersToContainedElements
{
    [Transaction(TransactionMode.Manual)]
    class RoomsFromLinkToMass : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument uidoc = uiApp.ActiveUIDocument;

            sl.clrLog();

            DocumentSet documents = uiApp.Application.Documents;

            Dictionary<string, Document> dict = new Dictionary<string, Document>();

            foreach (Document document in documents)
            {
                //Debug.WriteLine($"{document.Title} is Linked: {document.IsLinked}.");
                if (document.IsLinked && document.Title != doc.Title) dict.Add(document.Title, document);
            }

            if (dict.Count == 0) { Debug.WriteLine("No other documents found!"); return Result.Cancelled; }

            Debug.WriteLine($"Found {dict.Count} other documents.");

            BaseFormTableLayoutPanel_Basic ds = new BaseFormTableLayoutPanel_Basic(
                System.Windows.Forms.Cursor.Position.X,
                System.Windows.Forms.Cursor.Position.Y,
                dict.Select(x => x.Key).OrderBy(x => x).ToList());
            ds.ShowDialog();
            string docTitle = ds.strTR;
            if (docTitle == null) { Debug.WriteLine("Cancelled!"); return Result.Cancelled; }

            Document roomDoc = dict[docTitle];
            if (roomDoc == null) { Debug.WriteLine("Room source document not found!"); return Result.Cancelled; }

            RoomFilter roomFilter = new RoomFilter();
            var roomFC = new FilteredElementCollector(roomDoc)
                .WherePasses(roomFilter);
            //    .Cast<Room>();
            sl.log("Number of rooms: " + roomFC.Count());

            using (TransactionGroup txGp = new TransactionGroup(doc))
            {
                txGp.Start("Rooms to Solids!");
                try
                {
                    Options opts = new Options();
                    opts.ComputeReferences = true;
                    opts.DetailLevel = ViewDetailLevel.Fine;

                    foreach (Room room in roomFC)
                    {
                        using (Transaction tx = new Transaction(doc))
                        {
                            tx.Start($"Room {room.Number}");

                            try
                            {
                                DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                                shape.ApplicationId = "Application id";
                                shape.ApplicationDataId = "Geometry object id";

                                List<GeometryObject> geomList = new List<GeometryObject>();

                                GeometryElement ge = room.get_Geometry(opts);
                                foreach (GeometryObject geomObj in ge)
                                {
                                    Solid geomSolid = geomObj as Solid;
                                    if (null != geomSolid)
                                    {
                                        try
                                        {
                                            if (shape.IsValidShape(new GeometryObject[] { geomSolid }))
                                            {
                                                geomList.Add(geomSolid);
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            continue;
                                        }
                                    }
                                }

                                if (geomList.Count == 0) { tx.RollBack(); continue; }

                                shape.SetShape(geomList);

                                var par = shape.LookupParameter("MC System Code");
                                if (par != null) par.Set(room.Number);
                            }
                            catch (Exception)
                            {
                                tx.RollBack();
                                continue;
                            }

                            tx.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    txGp.RollBack();
                    Debug.WriteLine(ex.ToString());
                    throw;
                }
                txGp.Assimilate();
            }

            return Result.Succeeded;
        }
    }
}
