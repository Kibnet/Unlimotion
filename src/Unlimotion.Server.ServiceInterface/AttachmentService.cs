using AutoMapper;
using Microsoft.Extensions.Configuration;
using Raven.Client.Documents.Session;
using ServiceStack;
using Unlimotion.Server.Domain;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Attachment;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Unlimotion.Server.ServiceInterface
{
    public class AttachmentService : Service
    {
        public IAsyncDocumentSession RavenSession { get; set; }
        public IMapper Mapper { get; set; }
        private string pref { get; set; }
        private string dirPatch { get; set; }

        public AttachmentService(IConfiguration configuration) 
        {
            dirPatch = configuration["FilesPath"];
            pref = "attachment/";
        }

        [Authenticate]
        public async Task<Stream> Get(GetAttachment request)
        {
            var filePath = Path.Combine(dirPatch, request.Id);

            if (!File.Exists(filePath))
            {
                throw HttpError.BadRequest("Error: file does not exist");
            }
           
            return File.OpenRead(filePath);
        }

        [Authenticate]
        public async Task<AttachmentMold> Post(SetAttachment request)
        {
            var session = Request.ThrowIfUnauthorized();
            var file = Request.Files.FirstOrDefault();

            if (file == null) throw HttpError.BadRequest("Error: No file to download");

            var stream = file?.InputStream;
            var fileId = $"{pref}{Guid.NewGuid()}";

            var fileUpload = new Attachment()
            {
                Id = fileId,
                FileName = file?.FileName,
                SenderId = session.UserAuthId,
                Hash = SaveFile(fileId.Replace(pref, string.Empty), stream),
                UploadDateTime = DateTimeOffset.Now,
                Size = stream.Length
            };

            await RavenSession.StoreAsync(fileUpload);
            await RavenSession.SaveChangesAsync();

            var mapped = Mapper.Map<AttachmentMold>(fileUpload);
            return mapped;
        }

        private string SaveFile(string fileId, Stream stream)
        {
            var filePath = Path.Combine(dirPatch, fileId);

            try
            {
                if (!Directory.Exists(dirPatch))
                {
                    Directory.CreateDirectory(dirPatch);
                }
            }
            catch
            {
                throw HttpError.BadRequest("Error on save file");
            }

            using (var fileStream = File.Create(filePath, (int)stream.Length))
            {
                const int bufferSize = 4194304;
                var data = new byte[bufferSize];

                stream.Seek(0, SeekOrigin.Begin);
                var hashString = string.Empty;

                while (stream.Position < stream.Length)
                {
                    var read = stream.Read(data, 0, bufferSize);
                    fileStream.Write(data, 0, read);

                    hashString += data.ToMd5Hash();
                }

                fileStream.Flush();
                return hashString;
            }
        }
    }
}
