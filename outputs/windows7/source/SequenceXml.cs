using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace HlsRecreation
{
    // Streaming structural quotas, not a byte-size gate. Valid large sequences
    // remain readable; invalid collection sizes are caught before XmlSerializer
    // allocates their object graphs. DTDs and external resolution stay disabled.
    public static class SequenceXml
    {
        private struct Element { public string Name; public int Children, Text; }
        public static XmlReaderSettings Settings()
        {
            return new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, MaxCharactersInDocument = 0, CloseInput = false };
        }
        public static void Preflight(Stream stream, string expectedRoot)
        {
            Stack<Element> stack = new Stack<Element>(); char[] chunk = new char[1024]; bool root = false;
            using (XmlReader reader = XmlReader.Create(stream, Settings()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (!root) { if (reader.LocalName != expectedRoot) throw new InvalidDataException("Unsupported sequence/library XML root."); root = true; }
                        if (reader.Depth > 32 || reader.LocalName.Length > 128 || reader.AttributeCount > 16) throw new InvalidDataException("Sequence XML structure is invalid.");
                        if (stack.Count > 0)
                        {
                            Element parent = stack.Pop(); parent.Children++;
                            if (parent.Children > ChildLimit(parent.Name)) throw new InvalidDataException(parent.Name + " exceeds its supported collection count. Split the data into smaller project/library entries.");
                            stack.Push(parent);
                        }
                        if (reader.HasAttributes)
                        {
                            for (int i = 0; i < reader.AttributeCount; i++) { reader.MoveToAttribute(i); if (reader.Value.Length > 4096) throw new InvalidDataException("Sequence XML attribute is too long."); }
                            reader.MoveToElement();
                        }
                        if (!reader.IsEmptyElement) stack.Push(new Element { Name = reader.LocalName });
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement) { if (stack.Count > 0) stack.Pop(); }
                    else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA || reader.NodeType == XmlNodeType.SignificantWhitespace)
                    {
                        if (stack.Count == 0) continue; Element current = stack.Pop(); string parent = stack.Count == 0 ? "" : stack.Peek().Name; int limit = TextLimit(current.Name, parent);
                        if (reader.CanReadValueChunk)
                        {
                            int read; while ((read = reader.ReadValueChunk(chunk, 0, chunk.Length)) != 0) { current.Text += read; if (current.Text > limit) throw new InvalidDataException(current.Name + " text exceeds its supported length."); }
                        }
                        else { current.Text += reader.Value.Length; if (current.Text > limit) throw new InvalidDataException(current.Name + " text exceeds its supported length."); }
                        stack.Push(current);
                    }
                }
            }
            if (!root) throw new InvalidDataException("Sequence/library file is empty.");
            stream.Position = 0;
        }
        private static int ChildLimit(string name)
        {
            switch (name)
            {
                case "Effects": return 100000;
                case "Channels": case "Members": case "Rows": return 32768;
                case "Groups": case "CustomEffects": case "Items": return 1024;
                case "Segments": return 4096;
                case "PixelPlanes": case "Parts": case "Frames": return 256;
                case "Controllers": return 256;
                case "Elements": return 2048;
                case "Assignments": return 32768;
                case "Points": return 128;
                case "Cells": case "Colors": return 1024;
                case "Curve": return 64;
                default: return 64;
            }
        }
        private static int TextLimit(string name, string parent)
        {
            if (name == "AudioFile" || name == "BackgroundImage" || name == "ControlFile" || name == "FseqFile") return 4096;
            if (name == "Name") return 256;
            if (name == "Id" || name == "TargetId") return 128;
            if (name == "string") return parent == "Colors" ? 7 : 128;
            if (name == "ColorHtml" || name == "SelectedColor") return 7;
            return 256;
        }
    }
}
