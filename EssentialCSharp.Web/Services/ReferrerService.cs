using Sqids;

namespace EssentialCSharp.Web.Services;

public class ReferrerService(SqidsEncoder<int> sqids)
{
    public string GenerateReferrerLink(string baseUrl, string userId)
    {
        string referrerId = sqids.Encode(1, 2, 3);
        string referrerLink = $"{baseUrl}?referrerId={referrerId}";

        // Store the referrerId and userId in the database for tracking
        SaveReferrerIdToDatabase(userId, referrerId);

        return referrerLink;
    }

    private void SaveReferrerIdToDatabase(string userId, string referrerId)
    {
        // Implement your database logic here
    }

    /// <summary>
    /// Track the referral in the database.
    /// </summary>
    /// <param name="referrerId">The referrer ID to track.</param>
    /// <returns>True if the referral was successfully tracked, otherwise false.</returns>
    public bool TrackReferral(string referrerId)
    {
        // Implement your logic to track the referral in the database

        if (sqids.Decode(referrerId) is [var decodedId] &&
    referrerId == sqids.Encode(decodedId))
        {
            // `incomingId` decodes into a single number and is canonical, here you can safely proceed with the rest of the logic
        }
        else
        {
            // consider `incomingId` invalid — e.g. respond with 404
        }
        IReadOnlyList<int> numbers = sqids.Decode(referrerId); // [1, 2, 3]
    }
}
