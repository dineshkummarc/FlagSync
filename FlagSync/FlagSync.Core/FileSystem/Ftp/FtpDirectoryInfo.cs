﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlagFtp;
using Rareform.Extensions;
using FlagSync.Core.FileSystem.Base;

namespace FlagSync.Core.FileSystem.Ftp
{
    /// <summary>
    /// Represents a directory on a FTP-server.
    /// </summary>
    [DebuggerDisplay("{FullName}")]
    public class FtpDirectoryInfo : IDirectoryInfo
    {
        private readonly FtpClient client;

        /// <summary>
        /// Gets the full name of the directory.
        /// </summary>
        public string FullName { get; private set; }

        /// <summary>
        /// Gets the name of the directory.
        /// </summary>
        public string Name
        {
            get { return Path.GetFileName(this.FullName); }
        }

        /// <summary>
        /// Gets the parent directory.
        /// </summary>
        public IDirectoryInfo Parent
        {
            get
            {
                string parentDirectoryName = Path.GetDirectoryName(this.FullName).Replace("\\", "//");
                Uri parentDirectoryUri = new Uri(parentDirectoryName);

                FlagFtp.FtpDirectoryInfo directory = this.client.GetDirectoryInfo(parentDirectoryUri);

                return new FtpDirectoryInfo(directory.FullName, this.client);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the directory exists.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the directory exists; otherwise, <c>false</c>.
        /// </value>
        public bool Exists
        {
            get { return true; }
        }

        /// <summary>
        /// Returns a list of all files in the directory.
        /// </summary>
        /// <returns>
        /// The files in the directory.
        /// </returns>
        /// <exception cref="System.UnauthorizedAccessException">
        /// The exception that is thrown if the directory is locked.
        ///   </exception>
        public IEnumerable<IFileInfo> GetFiles()
        {
            return this.client
                .GetFiles(new Uri(this.FullName))
                .Where(file => file.Name != ".ftpquota")
                .Select(file => new FtpFileInfo(file.FullName, file.LastWriteTime, file.Length, this.client))
                .Cast<IFileInfo>()
                .ToList();
        }

        /// <summary>
        /// Returns a list of all directories in the directory.
        /// </summary>
        /// <returns>
        /// The directories in the directory.
        /// </returns>
        /// <exception cref="System.UnauthorizedAccessException">
        /// The exception that is thrown if the directory is locked.
        ///   </exception>
        public IEnumerable<IDirectoryInfo> GetDirectories()
        {
            return this.client
                .GetDirectories(new Uri(this.FullName))
                .Select(directory => new FtpDirectoryInfo(directory.FullName, this.client))
                .Cast<IDirectoryInfo>()
                .ToList();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpDirectoryInfo"/> class.
        /// </summary>
        /// <param name="fullName">The full name.</param>
        /// <param name="client">The client.</param>
        public FtpDirectoryInfo(string fullName, FtpClient client)
        {
            fullName.ThrowIfNull(() => fullName);
            client.ThrowIfNull(() => client);

            this.FullName = fullName;
            this.client = client;
        }
    }
}