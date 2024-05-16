/** @file Album.cs
 *  @brief 相簿資料結構

 *  這個資料結構用於裝載從 .epub 中解壓縮出的 XML 資訊，並且存放為複合結構，這裡的資訊幾乎都不會用來
 *  做綁定用，所以使用公開資料成員即可，不需要宣告為屬性。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/7 */

using System;
using System.Xml;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Windows;
using System.Diagnostics;

namespace BrowseEpub
{
    public sealed class Album
    {
        /// <summary>
        ///  這個相簿的檔案資訊。
        /// </summary>
        public String PathName = String.Empty;

        /// <summary>
        ///  相簿基本資訊。
        /// </summary>
        public String Identifier = String.Empty;
        public String Title = String.Empty;
        public String Author = String.Empty;
        public String Location = String.Empty;

        /// <summary>
        ///  相簿日期第一天與最後一天。
        /// </summary>
        public DateTime FirstDate = DateTime.Now;
        public DateTime LastDate = DateTime.Now;

        /// <summary>
        ///  篇章目錄。
        /// </summary>
        public List<TocItem> Toc = new List<TocItem>();
        public TocItem CurTocItem = null;

        /// <summary>
        ///  解壓縮過程中發生的錯誤訊息。
        /// </summary>
        public String LastError = String.Empty;

        public Album()
        {
            /// 把前言加入到 Toc 當中:
            TocItem preface = new TocItem(this);
            preface.Title = Properties.Resources.Preface;
            preface.HtmlFileName = "title.xhtml";
            preface.IsChapter = false;
            Toc.Add(preface);
        }

        /// <summary>
        ///  根據檔名尋找 TocItem。
        /// </summary>
        public int IndexOfHtmlFile(String fileName)
        {
            int index = 0;


            foreach (TocItem item in Toc)
            {
                if (item.HtmlFileName.Equals(fileName)) {  return index;  }
                ++index;
            }

            return -1;
        }

        /// <summary>
        ///  從 PathName 解壓縮 XML 檔案並且組成 Album 資料結構。
        /// </summary>
        public Boolean LoadXml()
        {
            ZipArchive archive = null;
            FileStream fileStream = null;
            App app = Application.Current as App;
            LastError = String.Empty;

            /// ///////////////////////////////////////////////////////////////////////////////////
            /// 開啟 PathName 檔案並且取得 ZipArchive 物件，以便後續的解壓縮。
            if ((String.IsNullOrEmpty(PathName) == true) || (File.Exists(PathName) == false))
            {   LastError = Properties.Resources.FileNotFound;  return false;  }

            Debug.WriteLine("Analyzing file: " + PathName + "...");

            try
            {
                fileStream = File.OpenRead(PathName);
                archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
            }
            catch (Exception ex)
            {
                archive = null;
                LastError = ex.Message;
                Debug.WriteLine(LastError);
            }

            if (archive == null)
            {
                if (fileStream != null) {  fileStream.Close();  }
                return false;
            }

            /// ///////////////////////////////////////////////////////////////////////////////////
            /// 解讀 EPUB/album.xml 檔案。
            ZipArchiveEntry entry = archive.GetEntry("EPUB/album.xml");
            if (entry == null)
            {
                LastError = Properties.Resources.NotAnAlbumEpub;
                Debug.WriteLine("EPUB/album.xml is not found.");
                app.ThreadIsRunning = false;
                if (fileStream != null) {  fileStream.Close();  }
                return false;
            }

            using (Stream stream = entry.Open())
            {
                XmlDocument doc = new XmlDocument();
                try {  doc.Load(stream);  }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Debug.WriteLine(LastError);
 
                    app.ThreadIsRunning = false;
                    if (fileStream != null) {  fileStream.Close();  }
                    return false;
                }

                /// 從 ZipArchiveEntry 解壓縮的串流中解讀 album.xml 檔案:
                Identifier = doc.DocumentElement.GetAttribute("identifier");

                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Element)
                    {
                        XmlElement element = node as XmlElement;
                        if (element.Name.Equals("title")) {  Title = element.InnerText; }
                        else if (element.Name.Equals("firstdate"))
                        {
                            DateTime dt = DateTime.Parse(element.InnerText);
                            FirstDate = dt;  LastDate = dt;
                        }
                        else if (element.Name.Equals("lastdate"))
                        {
                            DateTime dt = DateTime.Parse(element.InnerText);
                            LastDate = dt;
                        }
                        else if (element.Name.Equals("location")) {  Location = element.InnerText;  }
                        else if (element.Name.Equals("author"))   {  Author = element.InnerText;  }
                        else if (element.Name.Equals("chapters"))
                        {
                            /* 相簿章節資訊
                            <chapters>
                                <chapter dir="章節路徑(子目錄名稱)">章節標題</chapter>
                            </ chapters > */
                            foreach (XmlNode child in element.ChildNodes)
                            {
                                if (child.NodeType == XmlNodeType.Element)
                                {
                                    TocItem chapter = new TocItem(this);
                                    XmlElement ele = child as XmlElement;
                                    chapter.Title = ele.InnerText;
                                    chapter.Directory = ele.GetAttribute("dir");
                                    chapter.HtmlFileName = chapter.Directory + ".xhtml";
                                    Toc.Add(chapter);
                                }
                            }
                        }
                    }
                }
            }

            /// ///////////////////////////////////////////////////////////////////////////////////
            /// 解讀 EPUB/(dir)/chapter.xml 檔案。
            foreach (TocItem chapter in Toc)
            {
                if (chapter.IsChapter == true)
                {
                    String zipEntryPath = "EPUB/" + chapter.Directory + "/chapter.xml";
                    entry = archive.GetEntry(zipEntryPath);
                    if (entry != null )
                    {
                        using (Stream stream = entry.Open())
                        {
                            XmlDocument doc = new XmlDocument();
                            try {  doc.Load(stream);  }
                            catch (Exception ex)
                            {
                                LastError = ex.Message;
                                Debug.WriteLine(LastError);
                                doc = null;
                            }

                            if (doc != null)
                            {
                                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                                {
                                    if (node.NodeType == XmlNodeType.Element)
                                    {
                                        XmlElement element = node as XmlElement;
                                        if (element.Name.Equals("paragraph"))
                                        {
                                            Paragraph para = new Paragraph(chapter);
                                            chapter.Paragraphs.Add(para);

                                            foreach (XmlNode child in element.ChildNodes)
                                            {
                                                if (child.NodeType == XmlNodeType.Element)
                                                {
                                                    XmlElement ele = child as XmlElement;
                                                    if (ele.Name.Equals("title"))
                                                    {   para.Title = ele.InnerText;  }
                                                    else if (ele.Name.Equals("context"))
                                                    {   para.Context = ele.InnerText;  }
                                                    else if (ele.Name.Equals("date"))
                                                    {
                                                        if (ele.GetAttribute("visible").Equals("true"))
                                                        {   para.DateIsVisible = true;  }

                                                        try
                                                        {
                                                            DateTime dt = DateTime.Parse(ele.InnerText);
                                                            para.Date = dt;
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            LastError = ex.Message;
                                                            Debug.WriteLine(LastError);
                                                        }
                                                    }
                                                    else if (ele.Name.Equals("location"))
                                                    {
                                                        para.Location = ele.InnerText;
                                                        if (ele.GetAttribute("visible").Equals("true"))
                                                        {   para.LocationIsVisible = true;  }
                                                    }
                                                    else if (ele.Name.Equals("photo"))
                                                    {

                                                        Photo photo = new Photo(para, para.Photos.Count);
                                                        photo.FileName = ele.GetAttribute("file");
                                                        photo.Description = ele.InnerText;
                                                        para.Photos.Add(photo);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        LastError = zipEntryPath + " is not found.";
                        Debug.WriteLine(LastError);
                    }
                }
            }

            if (fileStream != null) {  fileStream.Close();  }
            Debug.WriteLine("Album " + Title + " has " + Toc.Count + " toc items.");
            return true;
        }
    }
}
