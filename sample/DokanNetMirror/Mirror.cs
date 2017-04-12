using DokanNet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using FileAccess = DokanNet.FileAccess;
using DotCMIS;
using DotCMIS.Client.Impl;
using DotCMIS.Client;

namespace DokanNetMirror
{
    public class Mirror : IDokanOperations
    {
        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                              FileAccess.Execute |
                                              FileAccess.GenericExecute | FileAccess.GenericWrite | FileAccess.GenericRead;

        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
                                                   FileAccess.Delete |
                                                   FileAccess.GenericWrite;

        private string cmisURL;
        private string user;
        private string password;

        private string driveLabel = "CMIS_DRIVE";

        /// <summary>
        /// CMIS session.
        /// </summary>
        private ISession cmisSession;

        private void InitCmisSession()
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters[SessionParameter.BindingType] = BindingType.AtomPub;
            parameters[SessionParameter.AtomPubUrl] = cmisURL;
            parameters[SessionParameter.User] = user;
            parameters[SessionParameter.Password] = password;

            SessionFactory factory = SessionFactory.NewInstance();
            IList<IRepository> repositories = factory.GetRepositories(parameters);
            cmisSession = repositories[0].CreateSession();

            Console.Out.WriteLine(cmisSession.RepositoryInfo.VendorName);
            Console.Out.WriteLine("cmisURL : " + cmisURL);
            Console.Out.WriteLine("user : " + user);
            Console.Out.WriteLine("password : " + password);

            driveLabel = cmisSession.RepositoryInfo.ProductName;
        }

        public Mirror(string cmisURL, string user, string password)
        {
            this.cmisURL = cmisURL;
            this.user = user;
            this.password = password;
            InitCmisSession();
        }

        private string ToTrace(DokanFileInfo info)
        {
            if (info == null)
            {
                return "DokanFileInfo info is null";
            }

            var context = info.Context != null ? "<" + info.Context.GetType().Name + ">" : "<null>";

            return string.Format(CultureInfo.InvariantCulture, "{{{0}, {1}, {2}, {3}, {4}, #{5}, {6}, {7}}}",
                context, info.DeleteOnClose, info.IsDirectory, info.NoCache, info.PagingIo, info.ProcessId, info.SynchronousIo, info.WriteToEndOfFile);
        }

        private string ToTrace(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString(CultureInfo.CurrentCulture) : "<null>";
        }

        private static int traceLineId = 1;

        private NtStatus Trace(string method, string fileName, DokanFileInfo info, NtStatus result, params string[] parameters)
        {
            var extraParameters = parameters != null && parameters.Length > 0 ? ", " + string.Join(", ", parameters) : string.Empty;

#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {1}('{2}', {3}{4}) -> {5}",
                traceLineId++, method, fileName, ToTrace(info), extraParameters, result));
#endif

            return result;
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info,
                                  FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
                                  NtStatus result)
        {
#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {1}('{2}', {3}, [{4}], [{5}], [{6}], [{7}], [{8}]) -> {9}",
                traceLineId++, method, fileName, ToTrace(info), access, share, mode, options, attributes, result));
#endif

            return result;
        }

        #region Implementation of IDokanOperations

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
                                      DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");

            bool pathExists = true;
            bool pathIsDirectory = false;
            bool readWriteAttributes = (access & DataAccess) == 0;
            bool readAccess = (access & DataWriteAccess) == 0;
            ICmisObject cmisObject = null;

            try
            {
                cmisObject = cmisSession.GetObjectByPath(cmisPath);
                pathIsDirectory = cmisObject is IFolder;
            }
            catch (DotCMIS.Exceptions.CmisObjectNotFoundException)
            {
                pathExists = false;
            }

            switch (mode)
            {
                case FileMode.Open:

                    if (pathExists)
                    {
                        if (readWriteAttributes || pathIsDirectory)
                        // check if driver only wants to read attributes, security info, or open directory
                        {
                            info.IsDirectory = pathIsDirectory;
                            info.Context = new object();
                            // must set it to someting if you return DokanError.Success

                            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
                        }
                    }
                    else
                    {
                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                    }
                    break;

                case FileMode.CreateNew:
                    if (pathExists)
                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileExists);
                    break;

                case FileMode.Truncate:
                    if (!pathExists)
                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                    break;

                default:
                    break;
            }

            try
            {
                //info.Context = new FileStream(path, mode, readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);
                info.Context = cmisObject;
            }
            catch (UnauthorizedAccessException) // don't have access rights  // TODO wont happen
            {
                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
            }

            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
        }

        public NtStatus OpenDirectory(string fileName, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            ICmisObject cmisObject = cmisSession.GetObjectByPath(cmisPath);
            
            if (cmisObject == null || ! (cmisObject is Folder))
            {
                return Trace("OpenDirectory", fileName, info, DokanResult.PathNotFound);
            }

            /*try
            {
                new DirectoryInfo(path).EnumerateFileSystemInfos().Any(); // you can't list the directory
            }
            catch (UnauthorizedAccessException)
            {
                return Trace("OpenDirectory", fileName, info, DokanResult.AccessDenied);
            }*/
            return Trace("OpenDirectory", fileName, info, DokanResult.Success);
        }

        public NtStatus CreateDirectory(string fileName, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            var baseDirectoryPath = cmisPath.Substring(0, cmisPath.LastIndexOf("/") + "/".Length);
            var newDirectoryName = cmisPath.Substring(baseDirectoryPath.Length);
            
            //if (Directory.Exists(GetPath(fileName)))  TODO port to CMIS, not urgent
            //    return Trace("CreateDirectory", fileName, info, DokanResult.FileExists);

            try
            {
                // Create CMIS folder.
                IFolder cmisFolder = (IFolder)cmisSession.GetObjectByPath(baseDirectoryPath);
                Dictionary<string, object> properties = new Dictionary<string, object>();
                properties.Add(PropertyIds.Name, newDirectoryName);
                properties.Add(PropertyIds.ObjectTypeId, "cmis:folder");
                cmisFolder.CreateFolder(properties);

                //Directory.CreateDirectory(GetPath(fileName));
                return Trace("CreateDirectory", fileName, info, DokanResult.Success);
            }
            catch (Exception) // TODO
            {
                return Trace("CreateDirectory", fileName, info, DokanResult.AccessDenied);
            }
        }

        public void Cleanup(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(string.Format(CultureInfo.CurrentCulture, "{0}('{1}', {2} - entering",
                    "Cleanup", fileName, ToTrace(info)));
#endif

            if (info.Context != null && info.Context is FileStream)
            {
                (info.Context as FileStream).Dispose();
            }
            info.Context = null;

            if (info.DeleteOnClose)
            {
                var cmisPath = fileName.Replace("\\", "/");
                bool pathExists = true;

                ICmisObject cmisObject = null;

                try
                {
                    cmisObject = cmisSession.GetObjectByPath(cmisPath);
                }
                /*catch (IOException) // TODO remove
                {
                    pathExists = false;
                }*/
                catch (DotCMIS.Exceptions.CmisObjectNotFoundException)
                {
                    pathExists = false;
                }

                if (pathExists)
                {
                    cmisObject.Delete(true);
                }
            }
            Trace("Cleanup", fileName, info, DokanResult.Success);
        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(string.Format(CultureInfo.CurrentCulture, "{0}('{1}', {2} - entering",
                    "CloseFile", fileName, ToTrace(info)));
#endif

            if (info.Context != null && info.Context is FileStream)
            {
                (info.Context as FileStream).Dispose();
            }
            info.Context = null;
            Trace("CloseFile", fileName, info, DokanResult.Success); // could recreate cleanup code here but this is not called sometimes
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            IDocument cmisDocument = (IDocument)cmisSession.GetObjectByPath(cmisPath);

            if (offset > int.MaxValue)
            {
                bytesRead = 0;
                return Trace("ReadFile", fileName, info, DokanResult.BufferOverflow, "out 0", offset.ToString(CultureInfo.InvariantCulture));
            }
            int offsetInt = (int)offset;
            
            //if (info.Context == null) // memory mapped read
            //{
                //using (var stream = new FileStream(GetPath(fileName), FileMode.Open, System.IO.FileAccess.Read))
                using (var stream = cmisDocument.GetContentStream().Stream)
                {
                    //stream.Position = offset; // TODO not supported directly by CMIS it seems?
                    stream.Read(buffer, 0, offsetInt); // Workaround to go to the offset
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            /*}
            else // normal read
            {
                var stream = info.Context as FileStream;
                stream.Position = offset;
                bytesRead = stream.Read(buffer, 0, buffer.Length);
            }*/
            return Trace("ReadFile", fileName, info, DokanResult.Success, "out " + bytesRead.ToString(), offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            if (info.Context == null)
            {
                using (var stream = new FileStream("TODO GetPath(fileName)", FileMode.Open, System.IO.FileAccess.Write))
                {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            }
            else
            {
                var stream = info.Context as FileStream;
                stream.Write(buffer, 0, buffer.Length);
                bytesWritten = buffer.Length;
            }
            return Trace("WriteFile", fileName, info, DokanResult.Success, "out " + bytesWritten.ToString(), offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Flush();
                return Trace("FlushFileBuffers", fileName, info, DokanResult.Success);
            }
            catch (IOException)
            {
                return Trace("FlushFileBuffers", fileName, info, DokanResult.DiskFull);
            }
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            ICmisObject cmisObject = (IDocument)cmisSession.GetObjectByPath(cmisPath);

            fileInfo = new FileInformation
            {
                FileName = fileName,
                Attributes = cmisObject is Folder ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal,
                CreationTime = File.GetCreationTime(@"c:\config.sys"),
                LastAccessTime = File.GetCreationTime(@"c:\config.sys"),
                LastWriteTime = File.GetCreationTime(@"c:\config.sys"),
                Length = (cmisObject is Document) ?
                        (((Document)cmisObject).ContentStreamLength != null) ? (long)((Document)cmisObject).ContentStreamLength : 0
                        : 0,
            };
            return Trace("GetFileInformation", fileName, info, DokanResult.Success);
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");

            IFolder cmisFolder = (IFolder)cmisSession.GetObjectByPath(cmisPath);
            files = cmisFolder.GetChildren()
                .Select(cmisObject => new FileInformation
                {
                    Attributes = cmisObject is Folder ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal,
                    CreationTime = (DateTime)cmisObject.CreationDate,
                    LastAccessTime = DateTime.Now,
                    LastWriteTime = (DateTime)cmisObject.LastModificationDate,
                    Length = (cmisObject is Document) ?
                        (((Document)cmisObject).ContentStreamLength != null) ? (long)((Document)cmisObject).ContentStreamLength : 0
                        : 0,
                    FileName = cmisObject.Name
                }).ToArray();

            return Trace("FindFiles", fileName, info, DokanResult.Success);
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            try
            {
                File.SetAttributes("TODO GetPath(fileName)", attributes);
                return Trace("SetFileAttributes", fileName, info, DokanResult.Success, attributes.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace("SetFileAttributes", fileName, info, DokanResult.AccessDenied, attributes.ToString());
            }
            catch (FileNotFoundException)
            {
                return Trace("SetFileAttributes", fileName, info, DokanResult.FileNotFound, attributes.ToString());
            }
            catch (DirectoryNotFoundException)
            {
                return Trace("SetFileAttributes", fileName, info, DokanResult.PathNotFound, attributes.ToString());
            }
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, DokanFileInfo info)
        {
            try
            {
                string path = "TODO GetPath(fileName)";
                if (creationTime.HasValue)
                    File.SetCreationTime(path, creationTime.Value);

                if (lastAccessTime.HasValue)
                    File.SetLastAccessTime(path, lastAccessTime.Value);

                if (lastWriteTime.HasValue)
                    File.SetLastWriteTime(path, lastWriteTime.Value);

                return Trace("SetFileTime", fileName, info, DokanResult.Success, ToTrace(creationTime), ToTrace(lastAccessTime), ToTrace(lastWriteTime));
            }
            catch (UnauthorizedAccessException)
            {
                return Trace("SetFileTime", fileName, info, DokanResult.AccessDenied, ToTrace(creationTime), ToTrace(lastAccessTime), ToTrace(lastWriteTime));
            }
            catch (FileNotFoundException)
            {
                return Trace("SetFileTime", fileName, info, DokanResult.FileNotFound, ToTrace(creationTime), ToTrace(lastAccessTime), ToTrace(lastWriteTime));
            }
        }

        public NtStatus DeleteFile(string fileName, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            bool pathExists = true;
            bool pathIsFile = true;

            ICmisObject cmisObject = null;

            try
            {
                cmisObject = cmisSession.GetObjectByPath(cmisPath);
                pathIsFile = !(cmisObject is IFolder);
            }
            /*catch (IOException) // TODO remove
            {
                pathExists = false;
            }*/
            catch (DotCMIS.Exceptions.CmisObjectNotFoundException)
            {
                pathExists = false;
            }

            return Trace("DeleteFile", fileName, info, pathExists && pathIsFile ? DokanResult.Success : DokanResult.FileNotFound);

            //return Trace("DeleteFile", fileName, info, File.Exists("TODO GetPath(fileName)") ? DokanResult.Success : DokanResult.FileNotFound);
            // we just check here if we could delete the file - the true deletion is in Cleanup
        }

        public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            bool pathExists = true;
            bool pathIsDirectory = true;

            ICmisObject cmisObject = null;

            try
            {
                cmisObject = cmisSession.GetObjectByPath(cmisPath);
                pathIsDirectory = cmisObject is IFolder;
            }
            /*catch (IOException) // TODO remove
            {
                pathExists = false;
            }*/
            catch (DotCMIS.Exceptions.CmisObjectNotFoundException)
            {
                pathExists = false;
            }

            return Trace("DeleteDirectory", fileName, info, pathExists && pathIsDirectory ? DokanResult.Success : DokanResult.DirectoryNotEmpty);
            //return Trace("DeleteDirectory", fileName, info, Directory.EnumerateFileSystemEntries("TODO GetPath(fileName)").Any() ? DokanResult.DirectoryNotEmpty : DokanResult.Success);
            // if dir is not empty it can't be deleted
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            string oldpath = oldName.Replace("\\", "/");
            string newpath = newName.Replace("\\", "/");

            ICmisObject oldObject = cmisSession.GetObjectByPath(oldpath);
            oldObject.Rename(newpath);

            /*bool exist = false;  // TODO replace the quick implementation above by something more like the decision tree below
            if (info.IsDirectory)
                exist = Directory.Exists(newpath);
            else
                exist = File.Exists(newpath);

            if (!exist)
            {
                info.Context = null;
                if (info.IsDirectory)
                    Directory.Move(oldpath, newpath);
                else
                    File.Move(oldpath, newpath);
                return Trace("MoveFile", oldName, info, DokanResult.Success, newName, replace.ToString(CultureInfo.InvariantCulture));
            }
            else if (replace)
            {
                info.Context = null;

                if (!info.IsDirectory)
                    File.Delete(newpath);
                else
                    Directory.Delete(newpath, true);

                if (info.IsDirectory)
                    Directory.Move(oldpath, newpath);
                else
                    File.Move(oldpath, newpath);
                return Trace("MoveFile", oldName, info, DokanResult.Success, newName, replace.ToString(CultureInfo.InvariantCulture));
            }*/
            return Trace("MoveFile", oldName, info, DokanResult.FileExists, newName, replace.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).SetLength(length);
                return Trace("SetEndOfFile", fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("SetEndOfFile", fileName, info, DokanResult.DiskFull, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).SetLength(length);
                return Trace("SetAllocationSize", fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("SetAllocationSize", fileName, info, DokanResult.DiskFull, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Lock(offset, length);
                return Trace("LockFile", fileName, info, DokanResult.Success, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("LockFile", fileName, info, DokanResult.AccessDenied, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Unlock(offset, length);
                return Trace("UnlockFile", fileName, info, DokanResult.Success, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("UnlockFile", fileName, info, DokanResult.AccessDenied, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus GetDiskFreeSpace(out long free, out long total, out long used, DokanFileInfo info)
        {
            // In the CMIS world, we have no way to know how much space is left.
            used = 500*1000*1000; // 500 MB TODO: Add recursively the sizes of all documents, it could take a very long time though.
            free = 1000*1000*1000; // 1 GB
            total = used + free;
            return Trace("GetDiskFreeSpace", null, info, DokanResult.Success, "out " + free.ToString(), "out " + total.ToString(), "out " + used.ToString());
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
                                                out string fileSystemName, DokanFileInfo info)
        {
            volumeLabel = driveLabel;
            fileSystemName = driveLabel;

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace("GetVolumeInformation", null, info, DokanResult.Success, "out " + volumeLabel, "out " + features.ToString(), "out " + fileSystemName);
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            var cmisPath = fileName.Replace("\\", "/");
            ICmisObject cmisObject = (IDocument)cmisSession.GetObjectByPath(cmisPath);

            try
            {
                // TODO
                //security = info.IsDirectory
                //               ? (FileSystemSecurity)Directory.GetAccessControl("TODO GetPath(fileName)")
                //               : File.GetAccessControl("TODO GetPath(fileName)");
                if (cmisObject is IFolder)
                {
                    //security = new System.Security.AccessControl.DirectorySecurity();
                    //security.AccessRightType = new AccessRig
                    security = Directory.GetAccessControl(Constants.TEMPLATE_FOLDER); // TODO create from scratch rather than using a template
                }
                else
                {
                    security = File.GetAccessControl(Constants.TEMPLATE_FILE); // TODO create from scratch rather than using a template
                }

                return Trace("GetFileSecurity", fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                security = null;
                return Trace("GetFileSecurity", fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            try
            {
                if (info.IsDirectory)
                {
                    Directory.SetAccessControl("TODO GetPath(fileName)", (DirectorySecurity)security);
                }
                else
                {
                    File.SetAccessControl("TODO GetPath(fileName)", (FileSecurity)security);
                }
                return Trace("SetFileSecurity", fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace("SetFileSecurity", fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
        }

        public NtStatus Unmount(DokanFileInfo info)
        {
            return Trace("Unmount", null, info, DokanResult.Success);
        }

        public NtStatus EnumerateNamedStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, DokanFileInfo info)
        {
            streamName = String.Empty;
            streamSize = 0;
            return Trace("EnumerateNamedStreams", fileName, info, DokanResult.NotImplemented, enumContext.ToString(), "out " + streamName, "out " + streamSize.ToString());
        }

        #endregion Implementation of IDokanOperations
    }
}