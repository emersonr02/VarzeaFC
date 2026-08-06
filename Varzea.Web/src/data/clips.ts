import type { PendingTransferOffer, SeasonResult, TitleKind } from "../api/types";

export type ClipData =
  | { kind: "final"; season: SeasonResult; title: TitleKind }
  | { kind: "season"; season: SeasonResult }
  | { kind: "awards"; season: SeasonResult }
  | { kind: "retire"; season: SeasonResult }
  | { kind: "offer"; offer: PendingTransferOffer };

const CUP_TITLES: TitleKind[] = ["DomesticCup", "ContinentalPrimary", "ContinentalSecondary", "WorldCup"];

/**
 * A ordem importa: finais primeiro, resumo da liga depois. O varzea-lendas.html original
 * (POC aprovado) fazia o contrário — resumo da liga revelava tudo, e a final vinha depois,
 * o que "estraga o efeito" (HANDOFF §8). Aqui a tensão da final chega antes de qualquer
 * spoiler sobre o resto da temporada.
 */
export function buildClipsForSeason(season: SeasonResult): ClipData[] {
  const clips: ClipData[] = [];
  for (const title of CUP_TITLES) {
    if (season.titles.includes(title)) clips.push({ kind: "final", season, title });
  }
  clips.push({ kind: "season", season });
  if (season.titles.includes("BallonDOr") || season.titles.includes("TeamOfTheYear") || season.inTeamOfTheYear
    || season.titles.includes("KingOfAmerica") || season.titles.includes("SouthAmericanTeamOfTheYear")) {
    clips.push({ kind: "awards", season });
  }
  if (season.injury === "CareerEnding") {
    clips.push({ kind: "retire", season });
  }
  return clips;
}

export function buildClipsForSeasons(seasons: SeasonResult[]): ClipData[] {
  return seasons.flatMap(buildClipsForSeason);
}
