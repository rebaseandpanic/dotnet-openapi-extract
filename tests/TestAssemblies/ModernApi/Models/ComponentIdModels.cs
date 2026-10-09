// Types whose component ids collide by their short name: the generator must give each its
// own component, and every reference must lead to the schema of its own type.

namespace ModernApi.Models.Catalog
{
    /// <summary>Catalog item shared by both page types.</summary>
    public class Item
    {
        /// <summary>Item name.</summary>
        public string Name { get; set; } = string.Empty;
    }
}

namespace ModernApi.Models.Alpha
{
    /// <summary>Offset-based page of items.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    public class Page<T>
    {
        /// <summary>Items on this page.</summary>
        public List<T> AlphaItems { get; set; } = [];

        /// <summary>Total number of items.</summary>
        public int AlphaTotal { get; set; }
    }

    /// <summary>Alpha summary.</summary>
    public class Summary
    {
        /// <summary>Number of alpha entries.</summary>
        public int AlphaCount { get; set; }
    }
}

namespace ModernApi.Models.Beta
{
    /// <summary>Cursor-based page of items.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    public class Page<T>
    {
        /// <summary>First item of the page, if any.</summary>
        public T? BetaFirst { get; set; }

        /// <summary>Cursor of the next page.</summary>
        public string BetaCursor { get; set; } = string.Empty;
    }

    /// <summary>Beta summary.</summary>
    public class Summary
    {
        /// <summary>Beta label.</summary>
        public string BetaLabel { get; set; } = string.Empty;
    }
}
