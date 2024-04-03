using System;
using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;


namespace Unlimotion.Test
{
    public static class DocumentStoreHolder
    {
        private static Lazy<IDocumentStore> store = new Lazy<IDocumentStore>(CreateStore);

        public static IDocumentStore Store => store.Value;

        private static IDocumentStore CreateStore()
        {
            IDocumentStore store = new DocumentStore()
            {
                // Define the cluster node URLs (required)
                Urls = new[] { "http://localhost:8080"},

                // Define a default database (optional)
                Database = "Unlimotion",
                // Set conventions as necessary (optional)
                Conventions =
                {
                FindCollectionName = type => type.Name,
                },
                // Initialize the Document Store
            }.Initialize();

            return store;
        }
    }
}
