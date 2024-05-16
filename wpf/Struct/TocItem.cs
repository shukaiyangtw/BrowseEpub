/** @file TocItem.cs
 *  @brief 篇章資料結構

 *  這個資料結構用於裝載從 .epub 中解壓縮出的 XML 資訊，並提供由 Album 解壓縮 HTML 檔案的功能。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/7 */

using System;
using System.IO;
using System.Collections.Generic;

namespace BrowseEpub
{
    public sealed class TocItem
    {
        private Album m_album = null;
        public Album Album {  get { return m_album; }  }

        /// <summary>
        ///  篇章標題與內部(目錄)名稱。
        /// </summary>
        public String Title {  get; set;  }
        public String Directory = String.Empty;
        public Boolean IsChapter = true;

        /// <summary>
        ///  篇章中的段落。
        /// </summary>
        public List<Paragraph> Paragraphs = new List<Paragraph>();

        /// <summary>
        ///  產出的檔案名稱。
        /// </summary>
        public String HtmlFileName = String.Empty;

        public TocItem(Album album)
        {   m_album = album;  }

        /// 根據檔名尋找 Photo 物件:
        public Photo FindPhoto(String fileTitle)
        {
            foreach (Paragraph para in Paragraphs)
            {
                foreach (Photo photo in para.Photos)
                {
                    String s = Path.GetFileNameWithoutExtension(photo.FileName);
                    if (s.Equals(fileTitle)) {  return photo;  }
                }
            }

            return null;
        }

    }
}
