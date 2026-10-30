using Remnants.Core.Trade;

namespace Remnants.Net;

// ---- live trade: two connected players, in the lobby or allied in a mission (see TradeDesk) ----

/// <summary>Where a live trade happens: lobby goods come out of the colonies (home by courier), field goods out of the packs.</summary>
public enum TradeVenue { Lobby, Field }

/// <summary>C→S: open a trade window with <paramref name="ToPlayerId"/> (they get a <see cref="TradeInvite"/>).</summary>
public sealed record TradeRequest(int ToPlayerId) : Message;

/// <summary>C→S: answer an invite. Declining closes it for both.</summary>
public sealed record TradeRespond(int TradeId, bool Accept) : Message;

/// <summary>C→S: replace my side of the table. Any change unlocks (and unconfirms) both sides.</summary>
public sealed record TradeOffer(int TradeId, List<TradeGood> Goods) : Message;

/// <summary>C→S: lock (or unlock) my offer. Confirming needs both sides locked.</summary>
public sealed record TradeLock(int TradeId, bool Locked) : Message;

/// <summary>
/// C→S: confirm the locked trade. In the lobby the client has set its offer aside in escrow before sending this (the
/// server refunds it with <see cref="TradeCancelled"/> if the trade falls through). Once both confirm, the server commits.
/// </summary>
public sealed record TradeConfirm(int TradeId) : Message;

/// <summary>C→S: walk away. Both sides are refunded.</summary>
public sealed record TradeCancel(int TradeId) : Message;

/// <summary>S→C, to the invited player only.</summary>
public sealed record TradeInvite(int TradeId, int FromPlayerId, string FromName, TradeVenue Venue) : Message;

/// <summary>One side of the table as both traders see it.</summary>
public sealed record TradeSideView(int PlayerId, string Name, List<TradeGood> Goods, bool Locked, bool Confirmed);

/// <summary>
/// The whole table, after every change, to the two traders only. <paramref name="Accepted"/>: the invite was taken
/// (offers can be made); <paramref name="SecondsLeft"/> until it is cancelled for idling.
/// </summary>
public sealed record TradeView(int TradeId, TradeVenue Venue, bool Accepted, List<TradeSideView> Sides, double SecondsLeft);

public sealed record TradeUpdate(TradeView Trade) : Message;

/// <summary>
/// S→C: the trade is done, atomically for both. <paramref name="Give"/> left the receiver (lobby: settle the escrow);
/// <paramref name="Get"/> is theirs (lobby: home by courier over <paramref name="Legs"/> legs; field: already in the packs).
/// <paramref name="TxId"/> is unique per trade, so applying it twice does nothing.
/// </summary>
public sealed record TradeCommit(int TradeId, string TxId, TradeVenue Venue, int PartnerId, string PartnerName,
    List<TradeGood> Give, List<TradeGood> Get, int Legs) : Message;

/// <summary>S→C: the trade is off (cancelled, declined, timed out, a trader left, the goods were gone). Refund any escrow.</summary>
public sealed record TradeCancelled(int TradeId, string Reason) : Message;

// ---- the Trading Post: an async market on a persistent server (MarketHost, `--market`) ----

/// <summary>
/// C→S, first message: who I am. <paramref name="Key"/> is the player's locally generated secret (hex, 32–128 chars);
/// the server only keeps its hash, which is the player's market id.
/// </summary>
public sealed record MarketHello(int Version, string Name, string Key) : Message;

/// <summary>
/// C→S: post "give X for Y". The client has already taken <paramref name="Give"/> into escrow under
/// <paramref name="TxId"/>; posting the same id again returns the same listing (a safe retry).
/// <paramref name="Hours"/> 0 = the server's default lifetime.
/// </summary>
public sealed record MarketPost(int RequestId, string TxId, List<TradeGood> Give, List<TradeGood> Want, double Hours = 0) : Message;

/// <summary>Browse filter: free text over good names / seller, goods given or wanted by id, or only my own listings.</summary>
public sealed record MarketFilter(string? Text = null, string? GiveId = null, string? WantId = null, bool MineOnly = false);

public sealed record MarketList(int RequestId, MarketFilter? Filter = null) : Message;

/// <summary>C→S: take a listing. The client has escrowed the listing's Want under <paramref name="TxId"/> (retry-safe).</summary>
public sealed record MarketAccept(int RequestId, string TxId, int ListingId) : Message;

/// <summary>C→S: withdraw my listing; its goods come back as a claim.</summary>
public sealed record MarketCancel(int RequestId, int ListingId) : Message;

/// <summary>C→S: send me my waiting claims (also sent unasked after <see cref="MarketWelcome"/> and after every trade).</summary>
public sealed record MarketClaim(int RequestId) : Message;

/// <summary>C→S: these claims are safely in my colony (saved); the server forgets them. Acking twice is harmless.</summary>
public sealed record MarketClaimAck(List<string> ClaimIds) : Message;

/// <summary>S→C: connected as <paramref name="PlayerId"/> (the key's hash).</summary>
public sealed record MarketWelcome(int Version, string PlayerId, string Name, double ListingHours, int MaxOpenListings) : Message;

/// <summary>A public listing: the seller's display name only, never their id or key.</summary>
public sealed record MarketListing(int Id, string Seller, List<TradeGood> Give, List<TradeGood> Want,
    DateTimeOffset Posted, DateTimeOffset Expires, bool Mine);

public sealed record MarketListings(int RequestId, List<MarketListing> Listings, int Total) : Message;

/// <summary>
/// S→C: the verdict on a post, accept or cancel. <paramref name="Ok"/> false = nothing happened (refund the escrow);
/// <paramref name="Code"/> is machine-readable (<c>rate</c>, <c>cap</c>, <c>invalid</c>, <c>gone</c>, <c>own</c>, <c>version</c>).
/// </summary>
public sealed record MarketResult(int RequestId, bool Ok, string Message, MarketListing? Listing = null, string? Code = null) : Message;

/// <summary>Goods the Trading Post holds for me: a sale's price, a purchase, a cancelled or expired listing's refund.</summary>
public sealed record MarketClaimView(string ClaimId, List<TradeGood> Goods, string Reason);

public sealed record MarketClaims(List<MarketClaimView> Claims) : Message;
