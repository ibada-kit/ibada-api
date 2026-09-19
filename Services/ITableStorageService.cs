using Azure.Data.Tables;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ML.Charity.API.Client.Services
{
    public interface ITableStorageService<T> where T : class, ITableEntity, new()
    {
        // Add this method signature
        Task<IEnumerable<T>> QueryAsync(Expression<Func<T, bool>> filter);

        Task AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(string partitionKey, string rowKey);
        Task<T?> GetEntityAsync(string partitionKey, string rowKey);
        Task<T> AddEntityAsync(T entity);
        Task<List<T>> QueryEntitiesByPartitionAsync(string partitionKey);
        Task<List<T>> QueryEntitiesAsync(string? filter);
    }
}