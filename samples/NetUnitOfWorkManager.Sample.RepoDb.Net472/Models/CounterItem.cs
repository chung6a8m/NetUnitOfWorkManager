using RepoDb.Attributes;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472.Models
{
    [Map("[dbo].[NetUnitOfWorkCounter]")]
    public sealed class CounterItem
    {
        [Primary]
        [Identity]
        public long Id { get; set; }

        public int Value { get; set; }
    }
}
