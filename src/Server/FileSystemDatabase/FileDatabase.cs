﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.Collections.Generic;
using System.Linq;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Server.FileSystemNames;
using VsChromium.Server.FileSystemSnapshot;

namespace VsChromium.Server.FileSystemDatabase {
  /// <summary>
  /// Exposes am in-memory snapshot of the list of file names, directory names
  /// and file contents for a given <see cref="FileSystemTreeSnapshot"/> snapshot.
  /// </summary>
  public class FileDatabase : IFileDatabase {
    private readonly IDictionary<FileName, FileData> _files;
    private readonly IList<FileName> _fileNames;
    private readonly IDictionary<DirectoryName, DirectoryData> _directories;
    private readonly IList<DirectoryName> _directoryNames;
    private readonly IList<IFileContentsPiece> _fileContentsPieces;
    private readonly long _searchableFileCount;

    public FileDatabase(IDictionary<FileName, FileData> files,
                        IList<FileName> fileNames,
                        IDictionary<DirectoryName, DirectoryData> directories,
                        IList<DirectoryName> directoryNames,
                        IList<IFileContentsPiece> fileContentsPieces,
                        long searchableFileCount) {
      _files = files;
      _fileNames = fileNames;
      _directories = directories;
      _directoryNames = directoryNames;
      _fileContentsPieces = fileContentsPieces;
      _searchableFileCount = searchableFileCount;
    }

    public IDictionary<FileName, FileData> Files {
      get {
        return _files;
      }
    }

    public IDictionary<DirectoryName, DirectoryData> Directories {
      get {
        return _directories;
      }
    }

    public IList<FileName> FileNames {
      get {
        return _fileNames;
      }
    }

    public IList<DirectoryName> DirectoryNames {
      get {
        return _directoryNames;
      }
    }

    public IList<IFileContentsPiece> FileContentsPieces {
      get { return _fileContentsPieces; }
    }

    public long SearchableFileCount {
      get { return _searchableFileCount; }
    }

    public IEnumerable<FileExtract> GetFileExtracts(FileName filename, IEnumerable<FilePositionSpan> spans, int maxLength) {
      var fileData = GetFileData(filename);
      if (fileData == null)
        return Enumerable.Empty<FileExtract>();

      var contents = fileData.Contents;
      if (contents == null)
        return Enumerable.Empty<FileExtract>();

      return contents.GetFileExtracts(maxLength, spans);
    }

    public bool IsContainedInSymLink(DirectoryName name) {
      DirectoryData entry;
      if (!_directories.TryGetValue(name, out entry))
        return false;

      if (entry.DirectoryEntry.IsSymLink)
        return true;

      var parent = entry.DirectoryName.Parent;
      if (parent == null)
        return false;

      return IsContainedInSymLink(parent);
    }

    /// <summary>
    /// Return the <see cref="FileData"/> instance associated to <paramref name="filename"/> or null if not present.
    /// </summary>
    private FileData GetFileData(FileName filename) {
      FileData result;
      Files.TryGetValue(filename, out result);
      return result;
    }
  }
}
