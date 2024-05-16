/** @file Photo.cs
 *  @brief 相片資訊的資料結構。

 *  這個資料結構用於裝載照片檔案資訊，並且提供從相簿檔案中解壓出 BitmapImage 的功能。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/9 */

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowseEpub
{
    public sealed class Photo
    {
        private Paragraph m_para = null;
        public Paragraph Paragraph {  get { return m_para; }  }

        private Int32 m_photoIndex = 0;
        public Int32 Index {  get { return m_photoIndex; }  }

        /// <summary>
        /// 照片檔案名稱。
        /// </summary>
        private String m_fileName = String.Empty;
        public String FileName
        {
            get {  return m_fileName;  }
            set
            {
                m_fileName = value;
                m_zipEntry = "EPUB/" + m_para.Chapter.Directory + "/" + m_fileName;
                m_zipEntryThumb = "EPUB/" + m_para.Chapter.Directory + "/thumbs/" + m_fileName;
            }
        }

        /// <summary>
        ///  照片的簡短描述。
        /// </summary>
        public String Description {  get; set;   }

        /// <summary>
        ///  解壓縮所需的資訊。
        /// </summary>
        private String m_zipPathName = String.Empty;
        public String ZipPathName {  get { return m_zipPathName; }  }
        private String m_zipEntry = String.Empty;
        public String ZipEntry {  get { return m_zipEntry; }  }

        private String m_zipEntryThumb = String.Empty;
        private byte[] m_thumbBytes = null;
        public String ZipEntryThumb {  get {  return m_zipEntryThumb;  }  }

        public Photo(Paragraph para, Int32 photoIndex)
        {
            m_para = para;
            m_photoIndex = photoIndex;
            m_zipPathName = m_para.Chapter.Album.PathName;
        }

        /// <summary>
        ///  從 m_zipPathName/m_zipEntry 解壓縮照片並傳回之。
        /// </summary>
        public BitmapImage PhotoImageSrc
        {
            get
            {
                using (FileStream fileStream = File.OpenRead(m_zipPathName))
                {
                    using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        ZipArchiveEntry entry = archive.GetEntry(m_zipEntry);
                        if (entry != null)
                        {
                            using (Stream stream = entry.Open())
                            {
                                /// 解碼之前先將影像資料複製到記憶體當中:
                                MemoryStream ms = new MemoryStream();
                                stream.CopyTo(ms);
                                stream.Close();
                                ms.Seek(0, SeekOrigin.Begin);

                                /// 直接解壓縮影像為 BitmapImage 並且傳回之，會忽略 EXIF 資訊:
                                BitmapImage img = new BitmapImage();
                                img.CacheOption = BitmapCacheOption.None;
                                img.BeginInit();
                                img.StreamSource = ms;
                                img.EndInit();
                                return img;
                            }
                        }
                    }
                }

                return null;

            }
        }

        /// <summary>
        ///  從 m_zipPathName/m_zipEntryThumb 解壓縮照片縮圖並傳回之。
        /// </summary>
        public BitmapImage ThumbImageSrc
        {
            get
            {
                /// 檢查縮圖檔案是否存在，存在的話將檔案資料(未解壓)複製到記憶體當中，以便多次使用:
                if (m_thumbBytes == null) {  PreloadThumbBytes();  }

                if (m_thumbBytes != null)
                {
                    Stream stream = new MemoryStream(m_thumbBytes);
                    BitmapImage img = new BitmapImage();
                    img.CacheOption = BitmapCacheOption.None;
                    img.BeginInit();
                    img.StreamSource = stream;
                    img.EndInit();
                    return img;
                }

                return UnzipZipEntry(m_zipEntryThumb);
            }
        }

        /// <summary>
        ///  預先由檔案中解壓出縮圖的檔案資料。
        /// </summary>
        public void PreloadThumbBytes()
        {
            using (FileStream fileStream = File.OpenRead(m_zipPathName))
            {
                using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = archive.GetEntry(m_zipEntryThumb);
                    if (entry != null)
                    {
                        using (Stream stream = entry.Open())
                        {
                            /// 解碼之前先將影像資料複製到記憶體當中:
                            MemoryStream ms = new MemoryStream();
                            stream.CopyTo(ms);
                            stream.Close();
                            ms.Seek(0, SeekOrigin.Begin);
                            m_thumbBytes = ms.ToArray();
                        }
                    }
                }
            }
        }

        /// <summary>
        ///  從 m_zipPathName/zipEntry 解壓縮圖片並傳回之。
        /// </summary>
        private BitmapImage UnzipZipEntry(String zipEntry)
        {
            using (FileStream fileStream = File.OpenRead(m_zipPathName))
            {
                using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = archive.GetEntry(zipEntry);
                    if (entry != null)
                    {
                        using (Stream stream = entry.Open())
                        {
                            /// 解碼之前先將影像資料複製到記憶體當中:
                            MemoryStream ms = new MemoryStream();
                            stream.CopyTo(ms);
                            stream.Close();
                            ms.Seek(0, SeekOrigin.Begin);

                            /// 解壓縮影像為 BitmapImage 並且傳回之:
                            BitmapImage img = new BitmapImage();
                            img.CacheOption = BitmapCacheOption.None;
                            img.BeginInit();
                            img.StreamSource = ms;
                            img.EndInit();
                            return img;
                        }
                    }
                }
            }

            return null;
        }
    }
}
