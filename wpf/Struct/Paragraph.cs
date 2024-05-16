/** @file Paragraph.cs
 *  @brief 段落資料結構

 *  這個資料結構用於裝載從 .epub 中解壓縮出的 XML 資訊。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/6 */

using System;
using System.Collections.Generic;

namespace BrowseEpub
{
    public sealed class Paragraph
    {
        private TocItem m_chapter = null;
        public TocItem Chapter {  get { return m_chapter; }  }

        /// <summary>
        ///  段落標題。
        /// </summary>
        private String m_title = String.Empty;
        public String Title
        {
            get {  return m_title; }
            set {  m_title = value;  }
        }

        /// <summary>
        ///  標註地點。
        /// </summary>
        public String Location { get; set; }
        public Boolean LocationIsVisible { get; set; }

        /// <summary>
        ///  標註拍照日期。
        /// </summary>
        public DateTime Date { get; set; }
        public Boolean DateIsVisible { get; set; }

        public String FirstDate
        {
            get
            {
                if ((DateIsVisible == true) && (Date != null))
                {  return Date.ToString("d");  }
                return String.Empty;
            }
        }

        /// <summary>
        ///  段落文字內容。
        /// </summary>
        public String Context { get; set; }

        /// <summary>
        ///  段落內的相片集。
        /// </summary>
        private List<Photo> m_photos = null;
        public List<Photo> Photos {  get { return m_photos; }  }

        public Paragraph(TocItem chapter)
        {
            m_chapter = chapter;
            m_photos = new List<Photo>();
        }

        /// <summary>
        ///  在照片集中加入照片，並且建立需要的聯繫。
        /// </summary>
        void AddPhoto(String fileName, String description)
        {
            Photo photo = new Photo(this, m_photos.Count);
            photo.FileName = fileName;
            photo.Description = description;
            m_photos.Add(photo);
        }
    }
}
