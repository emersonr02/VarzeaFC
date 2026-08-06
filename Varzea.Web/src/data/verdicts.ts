import type { TitleKind } from "../api/types";

/**
 * Recalibrado no Roadmap §9 Bloco 1: o AwardScale/AwardCap do motor caiu (dois prêmios
 * individuais novos — Rei da América, Equipe do Ano da América — passaram a empilhar
 * com Bola de Ouro/Equipe do Ano na mesma carreira, e precisou de um teto menor pra não
 * dominar o critério 3 do Monte Carlo). A escala comprimiu: mediana ~79, p99 ~293,
 * máximo observado em 10.000 carreiras ~490 (era ~100/~420/~630-720 antes do Bloco 1).
 * Estes limiares foram reescalados na mesma proporção (~0,7×) — sem isso os tiers 8-11
 * ficariam praticamente inatingíveis.
 */
export interface Verdict {
  min: number;
  tier: number;
  title: string;
  desc: string;
  needsBallonOr?: boolean;
}

export const VERDICTS: Verdict[] = [
  { min: -Infinity, tier: 1, title: "Jogador Regular", desc: "Ganhou a vida com a bola no pé, sem holofote — mas rodou bola em time grande de verdade." },
  { min: 25, tier: 2, title: "Sensação de Uma Temporada", desc: "Teve seu momento de brilho. A torcida lembra até hoje daquele ano." },
  { min: 50, tier: 3, title: "Estrela em Ascensão", desc: "Consistente, querido pela torcida, sempre disputado no mercado." },
  { min: 70, tier: 4, title: "Nome de Peso", desc: "Peça-chave em qualquer elenco. Manchete garantida em temporada boa." },
  { min: 105, tier: 5, title: "Top 50 da História", desc: "Entrou pra galeria. Nome que aparece em qualquer discussão séria sobre a posição." },
  { min: 155, tier: 6, title: "Referência da Posição", desc: "Quando alguém pensa na sua posição, pensa em você primeiro." },
  { min: 210, tier: 7, title: "Top 10 da História", desc: "Carreira de arrepiar. Poucos fizeram o que você fez." },
  { min: 280, tier: 8, title: "O Maior da Posição", desc: "Ninguém discute. Você é a régua que os outros tentam alcançar." },
  { min: 335, tier: 9, title: "Top 5 da História", desc: "A Bola de Ouro na estante prova: você não foi só bom, foi o melhor do mundo.", needsBallonOr: true },
  { min: 405, tier: 10, title: "Sempre no Papo de Maior de Todos", desc: "Estátua, documentário, aquele arrepio ao ouvir seu nome. Você virou história.", needsBallonOr: true },
  { min: 475, tier: 11, title: "O FENÔMENO", desc: "O nível mais raro. Não existe estatística que resuma — só existe assistir e acreditar.", needsBallonOr: true },
];

export function computeVerdict(score: number, titleCounts: Partial<Record<TitleKind, number>>): Verdict {
  const hasBallonOr = (titleCounts.BallonDOr ?? 0) > 0;
  let best = VERDICTS[0];
  for (const v of VERDICTS) {
    if (score >= v.min) {
      if (v.needsBallonOr && !hasBallonOr) continue;
      best = v;
    }
  }
  return best;
}
