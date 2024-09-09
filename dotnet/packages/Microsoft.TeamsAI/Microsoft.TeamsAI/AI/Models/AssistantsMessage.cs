﻿using System.ClientModel;
using Microsoft.Bot.Schema;
using OpenAI.Assistants;
using OpenAI.Files;


namespace Microsoft.Teams.AI.AI.Models
{
    /// <summary>
    /// Represents a message returned by the OpenAI Assistants API.
    /// </summary>
    public class AssistantsMessage : ChatMessage
    {
        /// <summary>
        /// The message contents from an assistants api response.
        /// </summary>
        public MessageContent MessageContent;

        /// <summary>
        /// Files attached to the assistants api message.
        /// </summary>
        public List<OpenAIFile>? AttachedFiles { get; }

        /// <summary>
        /// Creates an AssistantMessage.
        /// </summary>
        /// <param name="content">The Assistants API thread message.</param>
        /// <param name="fileClient">The OpenAI File client.</param>
        public AssistantsMessage(MessageContent content, FileClient? fileClient = null) : base(ChatRole.Assistant)
        {
            this.MessageContent = content;

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            string textContent = content.Text ?? "";
            MessageContext context = new();

            List<Task<ClientResult<BinaryData>>> fileContentDownloadTasks = new();
            List<Task<ClientResult<OpenAIFileInfo>>> fileInfoDownloadTasks = new();

            for (int i = 0; i < content.TextAnnotations.Count; i++)
            {
                TextAnnotation annotation = content.TextAnnotations[i];
                if (annotation?.TextToReplace != null)
                {
                    textContent = textContent.Replace(annotation.TextToReplace, $"[{i + 1}]");
                }

                if (annotation?.InputFileId != null)
                {
                    // Retrieve file info object
                    // Neither `content` or `title` is provided in the annotations.
                    context.Citations.Add(new("Content not available", $"File {i + 1}", annotation.InputFileId));
                }

                if (annotation?.OutputFileId != null && fileClient != null)
                {
                    // Files generated by code interpretor tool.
                    fileContentDownloadTasks.Add(fileClient.DownloadFileAsync(annotation.OutputFileId));
                    fileInfoDownloadTasks.Add(fileClient.GetFileAsync(annotation.OutputFileId));
                }
            }

            List<OpenAIFile> attachedFiles = new();
            if (fileContentDownloadTasks.Count > 0)
            {
                Task.WaitAll(fileContentDownloadTasks.ToArray());
                Task.WaitAll(fileInfoDownloadTasks.ToArray());

                // Create attachments out of these downloaded files
                // Wait for tasks to complete
                ClientResult<BinaryData>[] downloadedFileContent = fileContentDownloadTasks.Select((task) => task.Result).ToArray();
                ClientResult<OpenAIFileInfo>[] downloadedFileInfo = fileInfoDownloadTasks.Select((task) => task.Result).ToArray();

                for (int i = 0; i < downloadedFileContent.Length; i++)
                {
                    attachedFiles.Add(new OpenAIFile(downloadedFileInfo[i], downloadedFileContent[i]));
                }
            }

            this.AttachedFiles = attachedFiles;
            this.Attachments = _ConvertAttachedImagesToActivityAttachments(attachedFiles);

            this.Content = textContent;
            this.Context = context;
        }

        private List<Attachment> _ConvertAttachedImagesToActivityAttachments(List<OpenAIFile> attachedFiles)
        {
            List<Attachment> attachments = new();

            foreach (OpenAIFile file in attachedFiles)
            {
                string? mimetype = file.GetMimeType();
                string[] imageMimeTypes = new string[] { "image/png", "image/jpg", "image/jpeg", "image/gif" };
                if (mimetype == null)
                {
                    continue;
                }

                if (!imageMimeTypes.Contains(mimetype))
                {
                    // Skip non image file types
                    continue;
                }

                string imageBase64String = Convert.ToBase64String(file.FileContent.ToArray());
                attachments.Add(new Attachment
                {
                    Name = file.FileInfo.Filename,
                    ContentType = mimetype,
                    ContentUrl = $"data:image/png;base64,{imageBase64String}",
                });
            }

            return attachments;
        }
    }

    /// <summary>
    /// Represents an OpenAI File.
    /// </summary>
    public class OpenAIFile
    {
        /// <summary>
        /// Represents an OpenAI File information
        /// </summary>
        public OpenAIFileInfo FileInfo;

        /// <summary>
        /// Represents the contents of an OpenAI File
        /// </summary>
        public BinaryData FileContent;

        private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "c", "text/x-c" },
            { "cs", "text/x-csharp" },
            { "cpp", "text/x-c++" },
            { "doc", "application/msword" },
            { "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            { "html", "text/html" },
            { "java", "text/x-java" },
            { "json", "application/json" },
            { "md", "text/markdown" },
            { "pdf", "application/pdf" },
            { "php", "text/x-php" },
            { "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
            { "py", "text/x-python" },
            { "rb", "text/x-ruby" },
            { "tex", "text/x-tex" },
            { "txt", "text/plain" },
            { "css", "text/css" },
            { "js", "text/javascript" },
            { "sh", "application/x-sh" },
            { "ts", "application/typescript" },
            { "csv", "application/csv" },
            { "jpeg", "image/jpeg" },
            { "jpg", "image/jpeg" },
            { "gif", "image/gif" },
            { "png", "image/png" },
            { "tar", "application/x-tar" },
            { "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { "xml", "application/xml" }, // or "text/xml"
            { "zip", "application/zip" }
        };

        /// <summary>
        /// Initializes an instance of OpenAIFile
        /// </summary>
        /// <param name="fileInfo">The OpenAI File</param>
        /// <param name="fileContent">The OpenAI File contents</param>
        public OpenAIFile(OpenAIFileInfo fileInfo, BinaryData fileContent)
        {
            FileInfo = fileInfo;
            FileContent = fileContent;
        }

        /// <summary>
        /// Gets the file's mime type
        /// </summary>
        /// <returns>The file's mime type</returns>
        public string? GetMimeType()
        {
            bool hasExtension = FileInfo.Filename.Contains(".");
            if (!hasExtension)
            {
                return null;
            }

            string fileExtension = FileInfo.Filename.Split(new char[] { '.' }).Last();
            if (MimeTypes.TryGetValue(fileExtension, out string mimeType))
            {
                return mimeType;
            }
            else
            {
                return null;
            }
        }
    }
}
