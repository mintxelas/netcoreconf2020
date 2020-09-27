using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;

namespace Data
{
    public class SampleDbContext: DbContext
    {
        public SampleDbContext(DbContextOptions<SampleDbContext> options): base(options)
        {

        }

        public DbSet<Url> Urls { get; set; }
    }
}
