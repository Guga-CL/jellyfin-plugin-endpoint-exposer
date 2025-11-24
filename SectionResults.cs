using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Dto;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.EndpointExposer
{
    public static class SectionResults
    {
        public static QueryResult<BaseItemDto> GetResults()
        {
            Console.WriteLine($"[EndpointExposer] GetResults() START {DateTime.UtcNow}");
            return BuildDummyResult();
        }

        private static QueryResult<BaseItemDto> BuildDummyResult()
        {
            var items = new List<BaseItemDto>
            {
                new BaseItemDto
                {
                    Id = Guid.NewGuid(),
                    Name = "Hello Jellyfin!",
                    Type = BaseItemKind.Movie,
                    Overview = "Dummy item from plugin (with fake image tag).",
                    ProductionYear = DateTime.UtcNow.Year,
                    PremiereDate = DateTime.UtcNow,
                    ImageTags = new Dictionary<ImageType, string>
                    {
                        { ImageType.Primary, Guid.NewGuid().ToString() }
                    }
                }
            };

            Console.WriteLine("[EndpointExposer] Built items count: " + items.Count);

            return new QueryResult<BaseItemDto>
            {
                Items = items,
                TotalRecordCount = items.Count
            };
        }
    }
}
