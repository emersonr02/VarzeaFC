import type { DilemmaTarget } from "../api/types";

// Conteúdo narrativo variado dos "dilemas fictícios" (Roadmap §9 Bloco 2, corte de
// escopo fechado). O motor já decide QUAL valor de moral mexeu, se foi pra cima ou pra
// baixo, e uma variante (0-2) — aqui só escolhemos o texto correspondente.
const DILEMMA_LINES: Record<Exclude<DilemmaTarget, "None">, { positive: string[]; negative: string[] }> = {
  Team: {
    positive: [
      "🤝 Você organizou uma resenha com o elenco e o grupo saiu mais unido.",
      "🎉 Ajudou um companheiro em dificuldade e ganhou respeito no vestiário.",
      "💪 Puxou o time num treino difícil e todo mundo notou a liderança.",
    ],
    negative: [
      "😬 Uma bronca sua num treino não caiu bem com o grupo.",
      "🙄 Colegas acharam que você se achou depois de uma boa atuação.",
      "😤 Uma piada mal interpretada esfriou o clima no vestiário.",
    ],
  },
  Coach: {
    positive: [
      "📋 Sugeriu um ajuste tático que o técnico adorou.",
      "⏰ Chegou cedo pros treinos a semana toda e a comissão técnica reparou.",
      "👍 Aceitou bem uma crítica dura e o técnico valorizou a maturidade.",
    ],
    negative: [
      "😠 Discutiu uma substituição na cara do técnico.",
      "📵 Chegou atrasado num treino e o técnico não gostou nada.",
      "🤨 Reclamou publicamente de uma escalação.",
    ],
  },
  Crowd: {
    positive: [
      "📸 Um gesto de carinho com um torcedor viralizou nas redes.",
      "❤️ Visitou o hospital do clube numa folga e a torcida amou.",
      "🎤 Uma entrevista sincera conquistou os torcedores.",
    ],
    negative: [
      "📉 Um post infeliz nas redes irritou parte da torcida.",
      "🚗 Boatos de festa na véspera de jogo pegaram mal.",
      "😒 Recusou fotos com torcedores depois de um treino.",
    ],
  },
};

export function dilemmaLine(target: Exclude<DilemmaTarget, "None">, positive: boolean, variant: number): string {
  const arr = positive ? DILEMMA_LINES[target].positive : DILEMMA_LINES[target].negative;
  return arr[variant % arr.length];
}
