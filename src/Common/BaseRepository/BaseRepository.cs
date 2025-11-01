using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Common.Infrastructure.Repositories
{
    public class BaseRepository<TEntity, TKey>
        where TEntity : class
        where TKey : notnull
    {
        protected readonly DbContext _context;
        protected readonly DbSet<TEntity> _dbSet;

        public BaseRepository(DbContext context)
        {
            _context = context;
            _dbSet = context.Set<TEntity>();
        }


        public virtual async Task<TEntity?> GetByIdAsync(TKey id)
        {
            return await _dbSet.FindAsync(id);
        }

        public virtual async Task<List<TEntity>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }


        public virtual async Task<TEntity> CreateAsync(TEntity entity)
        {
            // Force all DateTime -> UTC
            foreach (var prop in typeof(TEntity).GetProperties())
            {
                if (prop.PropertyType == typeof(DateTime))
                {
                    var value = (DateTime)prop.GetValue(entity);
                    if (value.Kind == DateTimeKind.Unspecified)
                        prop.SetValue(entity, DateTime.SpecifyKind(value, DateTimeKind.Utc));
                }
                else if (prop.PropertyType == typeof(DateTime?))
                {
                    var value = (DateTime?)prop.GetValue(entity);
                    if (value.HasValue && value.Value.Kind == DateTimeKind.Unspecified)
                        prop.SetValue(entity, DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
                }
            }

            _dbSet.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }



        public virtual async Task<TEntity> UpdateAsync(TEntity entity)
        {
            _dbSet.Update(entity);
            await _context.SaveChangesAsync();
            return entity;
        }


        public virtual async Task<bool> DeleteAsync(TEntity entity)
        {
            _dbSet.Remove(entity);
            var result = await _context.SaveChangesAsync();
            return result > 0;
        }
    }
}
