// Sabor cosmético só — nomes de clube/adversário e eventos de "AO VIVO" das finais.
// Nada aqui influencia número real (gols, títulos, score): tudo isso já veio pronto e
// autoritativo da API. Portado do varzea-lendas.html original (POC aprovado).

const PLACES: Record<string, string[]> = {
  Brasil: ["Vila Nova", "Serra Alta", "Rio Manso", "Litoral Sul", "Vale Verde"],
  Argentina: ["Puerto Bravo", "Las Pampas", "Villa del Sol", "Costa Azul", "Monte Grande"],
  Portugal: ["Vale do Tejo", "Serra Dourada", "Costa Norte", "Vila Real", "Porto Alto"],
  França: ["Val d'Or", "Rive Gauche", "Mont Clair", "Côte Bleue", "Beau Rivage"],
  Espanha: ["Costa Brava", "Sierra Alta", "Puerto Real", "Vega Dorada", "Monte Claro"],
  Alemanha: ["Rheinfeld", "Bergtal", "Nordpark", "Waldstadt", "Seehausen"],
  Inglaterra: ["Riverside", "Northgate", "Ashford", "Millfield", "Kingswood"],
  Itália: ["Monte Rosso", "Porto Chiaro", "Valle Verde", "Costa d'Oro", "Lago Blu"],
  Holanda: ["Zeewijk", "Nieuwland", "Duinpark", "Oosthaven", "Meerstad"],
  Uruguai: ["Costa Dorada", "Punta Alta", "Villa del Mar", "Sierra Este", "Bahía Norte"],
};
const TIER_TEMPLATES: Record<number, string[]> = {
  1: ["{p} Amador", "Operário do {p}", "Recreativo {p}", "União da Vila {p}"],
  2: ["Atlético {p}", "Esportivo {p}", "União {p}", "Grêmio {p}"],
  3: ["Real {p}", "Internacional {p}", "Metropolitano {p}", "{p} United", "Porto {p}"],
};
const INTERNATIONAL_CLUBS: Record<number, string[]> = {
  4: ["Independente Nordeste", "São Marcos EC", "Palmares United", "Real Nortista", "Litoral Atlético Clube"],
  5: ["Real Continental CF", "Metropolitano United", "Atlético Global", "Estrela Europa FC", "Porto Imperial CF"],
};
const FIRST_NAMES = ["Kaique", "Bruno", "Diego", "Rafa", "Theo", "Lucas", "Enzo", "Igor", "Caetano", "Vini", "Matteo", "Dario", "Sven", "Ollie", "Jorge", "Nuno", "Pietro", "Hugo", "Léo", "Tiago"];
const LAST_NAMES = ["Ferreira", "Duarte", "Almeida", "Rocha", "Bianchi", "Kessler", "Novak", "Dubois", "Ferrer", "Costa", "Salgado", "Vieira", "Moretti", "Larsen", "Sampaio", "Prado"];
const ALL_COUNTRIES = Object.keys(PLACES);

export const LEAGUE_NAME: Record<string, string> = {
  Brasil: "Brasileirão", Argentina: "Liga Profesional", Portugal: "Primeira Liga",
  França: "Ligue 1", Espanha: "La Liga", Alemanha: "Bundesliga",
  Inglaterra: "Premier League", Itália: "Serie A", Holanda: "Eredivisie", Uruguai: "Liga Uruguaya",
};
// Bug real encontrado: a tabela real (Roadmap pós-§9) simula a divisão 2 corretamente
// (clubTier 1-2) quando o jogador está lá, mas o front rotulava QUALQUER divisão com o
// nome da 1ª divisão — parecia "SpVgg Greuther Fürth em 5º na Bundesliga sem Bayern",
// quando na verdade era a 2. Bundesliga certinha, só com o nome errado.
export const SECOND_DIVISION_NAME: Record<string, string> = {
  Brasil: "Série B", Argentina: "Primera Nacional", Portugal: "Liga Portugal 2",
  França: "Ligue 2", Espanha: "La Liga 2", Alemanha: "2. Bundesliga",
  Inglaterra: "Championship", Itália: "Serie B", Holanda: "Eerste Divisie", Uruguai: "Segunda División",
};
export const DOMESTIC_CUP_NAME: Record<string, string> = {
  Brasil: "Copa do Brasil", Argentina: "Copa Argentina", Portugal: "Taça de Portugal",
  França: "Coupe de France", Espanha: "Copa del Rey", Alemanha: "DFB-Pokal",
  Inglaterra: "FA Cup", Itália: "Coppa Italia", Holanda: "KNVB Beker", Uruguai: "Copa Uruguay",
};
const SOUTH_AMERICA = ["Brasil", "Argentina", "Uruguai"];
export function continentalName(country: string, primary: boolean): string {
  const sa = SOUTH_AMERICA.includes(country);
  if (primary) return sa ? "Libertadores" : "Liga dos Campeões";
  return sa ? "Sul-Americana" : "Liga Europa";
}

function pick<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}
function randInt(a: number, b: number): number {
  return a + Math.floor(Math.random() * (b - a + 1));
}

export function randomPlayerName(): string {
  return `${pick(FIRST_NAMES)} ${pick(LAST_NAMES)}`;
}

export function buildClubName(country: string, tier: number): string {
  if (tier >= 4) return pick(INTERNATIONAL_CLUBS[Math.min(tier, 5)] ?? INTERNATIONAL_CLUBS[4]);
  const places = PLACES[country] ?? PLACES.Brasil;
  const tpl = pick(TIER_TEMPLATES[Math.max(1, Math.min(tier, 3))]);
  return tpl.replace("{p}", pick(places));
}

export function randomOpponentCountry(exclude?: string): string {
  const pool = exclude ? ALL_COUNTRIES.filter((c) => c !== exclude) : ALL_COUNTRIES;
  return pick(pool);
}

export interface MatchEvent {
  minute: number;
  side: "team" | "opp";
  who: string;
}

/** Distribui os gols reais da temporada (já vindos da API) num placar de final fictício
 * — o RESULTADO (você ganhou o título ou não) já é dado, isto só encena como aconteceu. */
export function buildFinalNarrative(playerName: string, won: boolean): { teamGoals: number; oppGoals: number; events: MatchEvent[] } {
  const teamGoals = won ? randInt(1, 3) : randInt(0, 2);
  const oppGoals = won ? Math.max(0, teamGoals - randInt(1, 2)) : Math.max(teamGoals + randInt(1, 2), teamGoals + 1);
  const events: MatchEvent[] = [];
  for (let g = 0; g < teamGoals; g++) {
    events.push({ minute: randInt(3, 90), side: "team", who: Math.random() < 0.5 ? playerName : "companheiro de equipe" });
  }
  for (let g = 0; g < oppGoals; g++) {
    events.push({ minute: randInt(3, 90), side: "opp", who: randomPlayerName() });
  }
  events.sort((a, b) => a.minute - b.minute);
  return { teamGoals, oppGoals, events };
}
