using System;
using System.Collections.Generic;
using System.Linq;

namespace ProductCatalog
{
    // Product entity
    public class Product
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Stock { get; init; }
        public DateTime ReleaseDate { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    }

    // Search criteria - open for extension (Open/Closed)
    public class ProductSearchCriteria
    {
        public string? NameContains { get; set; }
        public string? TagContains { get; set; }
        public string? Category { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public bool? InStock { get; set; }
        public DateTime? ReleasedAfter { get; set; }

        // Sorting and paging for performance
        public string? SortBy { get; set; }
        public bool Descending { get; set; }
        public int? Page { get; set; }
        public int PageSize { get; set; } = 20;
    }

    // Repository interface (Dependency Inversion)
    public interface IProductRepository
    {
        IEnumerable<Product> GetAll();
        // Expose a search optimized for deferred execution and pagination
        IEnumerable<Product> Search(ProductSearchCriteria criteria);
        // Example of a grouping operation using query syntax
        IEnumerable<(string Category, int Count)> GetCountsByCategory();
    }

    // In-memory repository implementation (Single Responsibility)
    public class InMemoryProductRepository : IProductRepository
    {
        private readonly List<Product> _products;

        public InMemoryProductRepository(IEnumerable<Product>? initial = null)
        {
            _products = initial?.ToList() ?? new List<Product>();
        }

        public IEnumerable<Product> GetAll() => _products;

        // Uses LINQ method syntax and lambda expressions to apply filters, ordering and paging
        public IEnumerable<Product> Search(ProductSearchCriteria criteria)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            // Deferred execution: build the query, only materialize when enumerated
            IEnumerable<Product> query = _products;

            if (!string.IsNullOrWhiteSpace(criteria.NameContains))
            {
                var term = criteria.NameContains.Trim();
                query = query.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Category))
            {
                var cat = criteria.Category.Trim();
                query = query.Where(p => string.Equals(p.Category, cat, StringComparison.OrdinalIgnoreCase));
            }

            if (criteria.MinPrice.HasValue)
                query = query.Where(p => p.Price >= criteria.MinPrice.Value);

            if (criteria.MaxPrice.HasValue)
                query = query.Where(p => p.Price <= criteria.MaxPrice.Value);

            if (criteria.InStock.HasValue)
                query = query.Where(p => criteria.InStock.Value ? p.Stock > 0 : p.Stock == 0);

            if (criteria.ReleasedAfter.HasValue)
                query = query.Where(p => p.ReleaseDate > criteria.ReleasedAfter.Value);

            // Tags: use Any with a lambda to allow case-insensitive tag matching
            if (!string.IsNullOrWhiteSpace(criteria.TagContains))
            {
                var tag = criteria.TagContains.Trim();
                query = query.Where(p => p.Tags != null && p.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));
            }

            // Sorting
            if (!string.IsNullOrWhiteSpace(criteria.SortBy))
            {
                query = ApplyOrdering(query, criteria.SortBy!, criteria.Descending);
            }

            // Demonstrate tag-based search using method syntax + lambdas
            Console.WriteLine();
            Console.WriteLine("Products tagged with 'office' (method syntax + lambdas):");
            var tagCriteria = new ProductSearchCriteria
            {
                TagContains = "office",
                SortBy = "name",
                Descending = false,
                Page = 0,
                PageSize = 20
            };

            // Use the repository's internal collection to demonstrate the same search logic
            var officeTagged = _products
                .Where(p => p.Tags != null && p.Tags.Any(t => string.Equals(t, tagCriteria.TagContains, StringComparison.OrdinalIgnoreCase)));

            officeTagged = ApplyOrdering(officeTagged, tagCriteria.SortBy!, tagCriteria.Descending);

            foreach (var p in officeTagged)
            {
                Console.WriteLine($"- {p.Name} | Tags: {string.Join(", ", p.Tags)} | {p.Category} | {p.Price:C}");
            }

            // Demonstrate LINQ query language for another scenario
            Console.WriteLine();
            Console.WriteLine("Furniture released after 2022 (query syntax):");
            var recentFurniture = from p in _products
                                  where p.Category == "Furniture" && p.ReleaseDate > new DateTime(2022, 1, 1)
                                  orderby p.ReleaseDate descending
                                  select p;

            foreach (var p in recentFurniture)
            {
                Console.WriteLine($"- {p.Name} | Released: {p.ReleaseDate:d} | {p.Price:C}");
            }

            // Paging - apply Skip/Take as late as possible for performance
            if (criteria.Page.HasValue && criteria.Page >= 0)
            {
                var skip = criteria.Page.Value * criteria.PageSize;
                query = query.Skip(skip).Take(criteria.PageSize);
            }

            // Materialize here to avoid multiple enumerations upstream
            return query.ToList();
        }

        // Example of grouping using LINQ query syntax
        public IEnumerable<(string Category, int Count)> GetCountsByCategory()
        {
            var query = from p in _products
                        group p by p.Category into g
                        orderby g.Key
                        select (Category: g.Key, Count: g.Count());

            return query.ToList();
        }

        private static IEnumerable<Product> ApplyOrdering(IEnumerable<Product> source, string sortBy, bool descending)
        {
            // Use a switch expression to keep open/closed (add more cases to extend)
            return (sortBy.Trim().ToLowerInvariant()) switch
            {
                "name" => descending ? source.OrderByDescending(p => p.Name) : source.OrderBy(p => p.Name),
                "price" => descending ? source.OrderByDescending(p => p.Price) : source.OrderBy(p => p.Price),
                "releasedate" => descending ? source.OrderByDescending(p => p.ReleaseDate) : source.OrderBy(p => p.ReleaseDate),
                "stock" => descending ? source.OrderByDescending(p => p.Stock) : source.OrderBy(p => p.Stock),
                _ => source // unknown sort -> preserve current ordering
            };
        }
    }

    // Service layer - composes repository calls and enforces business rules
    public class ProductService
    {
        private readonly IProductRepository _repository;

        public ProductService(IProductRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        // Facade for searching products
        public IEnumerable<Product> SearchProducts(ProductSearchCriteria criteria)
        {
            // Enforce maximum page size to protect performance
            if (criteria.PageSize <= 0) criteria.PageSize = 20;
            criteria.PageSize = Math.Min(criteria.PageSize, 200);

            return _repository.Search(criteria);
        }

        public IEnumerable<(string Category, int Count)> GetCountsByCategory() => _repository.GetCountsByCategory();
    }

    class Program
    {
        static void Main(string[] args)
        {
            // Seed sample data
            var products = new List<Product>
            {
                new Product { Name = "Portable Charger 10000mAh", Category = "Electronics", Price = 29.99m, Stock = 150, ReleaseDate = new DateTime(2023,1,10), Tags = new[] { "power", "mobile" } },
                new Product { Name = "Wireless Mouse", Category = "Electronics", Price = 19.50m, Stock = 80, ReleaseDate = new DateTime(2022,5,3), Tags = new[] { "pc", "accessory" } },
                new Product { Name = "Office Chair", Category = "Furniture", Price = 129.99m, Stock = 12, ReleaseDate = new DateTime(2021,11,20), Tags = new[] { "office" } },
                new Product { Name = "Standing Desk", Category = "Furniture", Price = 399.00m, Stock = 5, ReleaseDate = new DateTime(2024,2,15), Tags = new[] { "office", "ergonomic" } },
                new Product { Name = "Noise Cancelling Headphones", Category = "Electronics", Price = 199.99m, Stock = 0, ReleaseDate = new DateTime(2020,7,7), Tags = new[] { "audio" } },
                new Product { Name = "Ceramic Mug", Category = "Kitchen", Price = 7.99m, Stock = 240, ReleaseDate = new DateTime(2020,3,1), Tags = new[] { "drinkware" } }
            };

            IProductRepository repository = new InMemoryProductRepository(products);
            var service = new ProductService(repository);

            // Example: search using method syntax + lambdas
            var criteria = new ProductSearchCriteria
            {
                NameContains = "desk",
                MinPrice = 100,
                SortBy = "price",
                Descending = false,
                Page = 0,
                PageSize = 10
            };

            Console.WriteLine("Search results (method syntax + lambdas):");
            foreach (var p in service.SearchProducts(criteria))
            {
                Console.WriteLine($"- {p.Name} | {p.Category} | {p.Price:C} | Stock: {p.Stock}");
            }

            // Example: grouping using query syntax
            Console.WriteLine();
            Console.WriteLine("Counts by category (query syntax):");
            foreach (var group in service.GetCountsByCategory())
            {
                Console.WriteLine($"- {group.Category}: {group.Count}");
            }

            // Example: combining LINQ query syntax with method syntax for more advanced scenarios
            Console.WriteLine();
            Console.WriteLine("Top 3 most expensive electronics (mixed syntax):");
            var topElectronics = (from p in repository.GetAll()
                                  where p.Category == "Electronics"
                                  select p)
                                 .OrderByDescending(p => p.Price)
                                 .Take(3);

            foreach (var p in topElectronics)
            {
                Console.WriteLine($"- {p.Name} | {p.Price:C}");
            }
        }
    }
}
