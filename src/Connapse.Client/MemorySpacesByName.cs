using Connapse.Client.Api.Memory.Spaces.Item;

namespace Connapse.Client.Api.Memory.Spaces;

// Hand-written, beside the generated code. The spec types this path segment as a UUID because the
// space-management routes take one, so Kiota only indexes by Guid; the fact routes under it also
// take a space's name, such as "team-notes".
public partial class SpacesRequestBuilder
{
    /// <summary>A memory space by ID or name, for the fact routes that accept either.</summary>
    public ItemRequestBuilder this[string idOrName]
    {
        get
        {
            var urlTplParams = new Dictionary<string, object>(PathParameters) { ["%2Did"] = idOrName };
            return new ItemRequestBuilder(urlTplParams, RequestAdapter);
        }
    }
}
