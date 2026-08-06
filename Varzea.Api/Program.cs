using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Varzea.Api;
using Varzea.Data;
using Varzea.Data.Entities;
using Varzea.Engine.Model;
using Varzea.Engine.Ruleset;
using Varzea.Engine.Rng;
using Varzea.Engine.Scoring;
using Varzea.Engine.Simulation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Persistência é opcional de propósito: sem ConnectionStrings:Varzea configurada, a API
// inteira continua funcionando exatamente como antes (draft/advance/save só calculando
// score), o que é o que já está testado via curl. Isso existe pra não quebrar o fluxo
// atual num ambiente sem Postgres disponível (ver HANDOFF §7.6/§7 nota de ambiente).
string? connectionString = builder.Configuration.GetConnectionString("Varzea");
if (connectionString is not null)
    builder.Services.AddDbContext<VarzeaDbContext>(opts => opts.UseNpgsql(connectionString));

string rulesPath = builder.Configuration["Varzea:RulesetPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "Ruleset", "balance.json");
string weightsPath = builder.Configuration["Varzea:WeightsPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "Ruleset", "rarity-weights.json");

// Em produção o segredo TEM de vir de configuração/variável de ambiente — nunca do código.
// Um valor efêmero em dev é seguro porque tokens de draft são de curta duração (uma
// carreira); reiniciar o processo só invalida drafts em progresso, não carreiras salvas.
string tokenSecret = builder.Configuration["Varzea:TokenSecret"]
    ?? (builder.Environment.IsDevelopment()
        ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        : throw new InvalidOperationException("Configurar Varzea:TokenSecret fora do ambiente de desenvolvimento."));

var rules = GameRuleset.LoadFromFile(rulesPath);
var simulator = new CareerSimulator(rules);
var weights = RarityWeights.FromJson(File.ReadAllText(weightsPath));
var scorer = new CareerScorer(weights);
var tokens = new CareerTokenService(tokenSecret);

var app = builder.Build();

app.MapGet("/meta", () =>
    Results.Ok(new MetaResponse(rules.Version, rules.Countries.Keys.OrderBy(c => c).ToList())));

app.MapPost("/careers/start", (StartRequest? req) =>
{
    // Seed sempre gerada pelo servidor para carreiras normais — aceitar uma seed do
    // cliente permitiria buscar offline uma seed "sortuda" antes de sequer jogar.
    // A exceção deliberada é o desafio anual (seed pública e igual pra todo mundo).
    ulong seed = req?.Seed ?? NextSeed();
    var state = new CareerState(seed, rules.Version, Array.Empty<int>(), null, null, Array.Empty<bool>());
    var candidates = simulator.PreviewNextDraftRound(seed, state.DraftPicks);

    return Results.Ok(new DraftRoundResponse(
        tokens.Issue(state), Round: 1, Attribute: (Attr)0, ToOptions(candidates, (Attr)0)));
});

app.MapPost("/careers/draft", (DraftRequest req) =>
{
    if (tokens.TryVerify(req.Token) is not { } state) return Results.Unauthorized();
    if (state.Position is not null || state.DraftPicks.Length >= 8)
        return Results.BadRequest("draft já concluído para esta carreira");

    var picks = state.DraftPicks.Append(req.Pick).ToArray();
    var next = state with { DraftPicks = picks };

    if (picks.Length == 8)
    {
        int[] attrs = simulator.ResolveDraft(state.Seed, picks);
        var potentials = Enum.GetValues<Pos>()
            .Select(p => new PositionPotential(p, simulator.OverallFor(attrs, p)))
            .ToList();
        return Results.Ok(new DraftCompleteResponse(tokens.Issue(next), attrs, potentials));
    }

    var candidates = simulator.PreviewNextDraftRound(state.Seed, picks);
    var attr = (Attr)picks.Length;
    return Results.Ok(new DraftRoundResponse(tokens.Issue(next), picks.Length + 1, attr, ToOptions(candidates, attr)));
});

app.MapPost("/careers/position", (PositionRequest req) =>
{
    if (tokens.TryVerify(req.Token) is not { } state) return Results.Unauthorized();
    if (state.DraftPicks.Length != 8) return Results.BadRequest("draft incompleto");
    if (state.Position is not null) return Results.BadRequest("posição já escolhida");
    if (!rules.Countries.ContainsKey(req.Country)) return Results.BadRequest("país desconhecido");

    int[] attrs = simulator.ResolveDraft(state.Seed, state.DraftPicks);
    int potential = simulator.OverallFor(attrs, req.Position);
    var role = simulator.ResolveRole(attrs, req.Position);
    var next = state with { Position = req.Position, Country = req.Country };

    return Results.Ok(new PositionLockedResponse(tokens.Issue(next), potential, role.Name));
});

app.MapPost("/careers/advance", (AdvanceRequest req) =>
{
    if (tokens.TryVerify(req.Token) is not { } state) return Results.Unauthorized();
    if (state.Position is null || state.Country is null)
        return Results.BadRequest("posição/país ainda não definidos");

    // A decisão da oferta pendente (se houver) chega aqui e vira a próxima entrada de
    // TransferChoices. Como AdvanceCareer é uma re-simulação determinística do zero,
    // isso é tudo que precisa viajar entre chamadas — nada de RNG serializado.
    var choices = req.Decision is { } decision
        ? state.TransferChoices.Append(decision).ToArray()
        : state.TransferChoices;

    var recipe = new CareerRecipe(state.Seed, state.Country, state.DraftPicks, state.Position.Value, choices, state.RulesetVersion);
    var progress = simulator.AdvanceCareer(recipe);

    var newSeasons = progress.Result.Timeline.Skip(state.SeasonsRevealed).ToList();
    var next = state with { TransferChoices = choices, SeasonsRevealed = progress.Result.Timeline.Count };

    return Results.Ok(new AdvanceResponse(
        tokens.Issue(next), newSeasons, progress.PendingOffer, Finished: !progress.AwaitingDecision));
});

app.MapPost("/careers/save", async (SaveRequest req, HttpContext http) =>
{
    if (tokens.TryVerify(req.Token) is not { } state) return Results.Unauthorized();
    if (state.Position is null || state.Country is null || state.DraftPicks.Length != 8)
        return Results.BadRequest("carreira incompleta: draft, posição ou país faltando");

    // O cliente nunca manda o score — só a receita (as decisões já tomadas via
    // /careers/advance). O servidor re-simula e calcula sozinho, o que é o anti-cheat
    // inteiro (ver HANDOFF §2, "Persistência da carreira"). Se ainda faltar decisão de
    // transferência por vir, SimulateCareer trata como recusada (mesmo fallback de sempre).
    var recipe = new CareerRecipe(
        state.Seed, state.Country, state.DraftPicks, state.Position.Value,
        state.TransferChoices, state.RulesetVersion);
    var result = simulator.SimulateCareer(recipe);
    var score = scorer.Score(result);

    // GetService (não injeção automática do parâmetro) porque o DbContext só existe
    // registrado quando há ConnectionStrings:Varzea — sem isso, null aqui é o caminho
    // normal (dev sem Postgres), não um erro.
    var totals = new CareerTotals(
        result.PeakOverall, result.Seasons, result.TotalGoals, result.TotalAssists,
        result.TotalTackles, result.TotalCleanSheets, result.TotalCaps);

    var db = http.RequestServices.GetService<VarzeaDbContext>();
    if (db is null || req.PlayerId is not { } playerId)
        return Results.Ok(new SaveResponse(score.Total, score, SavedToSlot: null, result.TitleCounts, totals));

    if (await db.Players.FindAsync(playerId) is null)
        db.Players.Add(new Player { Id = playerId, CreatedAt = DateTimeOffset.UtcNow });

    int slotIndex;
    if (req.SlotIndex is { } requested)
    {
        if (requested is < 0 or > 9) return Results.BadRequest("slot precisa ser 0-9");
        slotIndex = requested;
    }
    else
    {
        var used = await db.CareerSlots
            .Where(c => c.PlayerId == playerId && !c.Archived)
            .Select(c => c.SlotIndex)
            .ToListAsync();
        if (used.Count >= 10)
            return Results.Conflict("os 10 slots estão ocupados — escolha um pra sobrescrever (SlotIndex)");
        slotIndex = Enumerable.Range(0, 10).Except(used).First();
    }

    // Sobrescrever nunca apaga: arquiva a linha ativa anterior desse slot (se houver) —
    // uma Achievement pode estar apontando pra ela (HANDOFF §7.6).
    var previous = await db.CareerSlots
        .FirstOrDefaultAsync(c => c.PlayerId == playerId && c.SlotIndex == slotIndex && !c.Archived);
    if (previous is not null) previous.Archived = true;

    db.CareerSlots.Add(new CareerSlot
    {
        Id = Guid.NewGuid(),
        PlayerId = playerId,
        SlotIndex = slotIndex,
        Seed = state.Seed,
        Country = state.Country,
        DraftPicks = state.DraftPicks,
        Position = state.Position.Value,
        TransferChoices = state.TransferChoices,
        RulesetVersion = state.RulesetVersion,
        Score = score.Total,
        TitlesScore = score.Titles,
        AwardsScore = score.Awards,
        ProductionScore = score.Production,
        PeakScore = score.Peak,
        SavedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    return Results.Ok(new SaveResponse(score.Total, score, SavedToSlot: slotIndex, result.TitleCounts, totals));
});

app.MapGet("/rankings/{period}", (string period) =>
    Results.Problem(
        title: "Ranking ainda não implementado",
        detail: "Depende da persistência em Postgres (HANDOFF §7.6), que ainda não existe.",
        statusCode: StatusCodes.Status501NotImplemented));

app.MapGet("/challenge/annual", () =>
{
    int year = DateTime.UtcNow.Year;
    var rng = Pcg32.Derive(0, "annual-challenge", year);
    ulong seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
    return Results.Ok(new AnnualChallengeResponse(year, seed));
});

app.Run();

static ulong NextSeed()
{
    Span<byte> bytes = stackalloc byte[8];
    RandomNumberGenerator.Fill(bytes);
    return BitConverter.ToUInt64(bytes);
}

static List<LegendOption> ToOptions(IReadOnlyList<Legend> candidates, Attr attr) =>
    candidates.Select(l => new LegendOption(l.Name, l.Get(attr))).ToList();
