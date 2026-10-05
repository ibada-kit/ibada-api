namespace ML.Charity.API.Client.DTOs
{
    /// <summary>
    /// Privacy-safe donor information showing only name and kit count.
    /// Excludes phone numbers, amounts, and other sensitive details.
    /// </summary>
    public class WardDonorDto
    {
        public string DonorName { get; set; } = string.Empty;
        public int KitCount { get; set; }
    }

    /// <summary>
    /// Paginated response for ward donor listings.
    /// </summary>
    public class WardDonorsResponse
    {
        public int WardNumber { get; set; }
        public int TotalDonors { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
        public List<WardDonorDto> Donors { get; set; } = new();

        /// <summary>
        /// Alias for Donors collection for consumer convenience.
        /// </summary>
        public List<WardDonorDto> Items => Donors;
    }
}
