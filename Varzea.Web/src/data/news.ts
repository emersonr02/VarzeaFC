// Manchetes do ticker da home — puro sabor, nenhuma influencia no motor. Tom da casa
// (várzea brasileira, deboche leve), não o de nenhum outro site: são frases próprias.
// A lista é fixa de propósito: é vitrine da home, não notícia da carreira do jogador
// (essa quem faz é o SeasonNewsFlash, com dados reais da temporada).
export interface Headline {
  tag: string;
  text: string;
}

// As TAGS também são da casa: a versão anterior usava o mesmo conjunto de rótulos de
// um simulador concorrente (Última Hora / Mercado / Oficial / Bastidores), o que
// deixava o ticker com cara de cópia mesmo com texto próprio.
export const HEADLINES: Headline[] = [
  { tag: "Rádio peão", text: "Moleque recusa peneira e avisa que só sai por time grande" },
  { tag: "Mercado da bola", text: "Empresário jura que o garoto “é o novo camisa 10 da seleção”" },
  { tag: "Na roda", text: "Mais um craque de fim de semana descobre o que é pré-temporada" },
  { tag: "Beira de campo", text: "Olheiro europeu é visto comendo pastel atrás do gol" },
  { tag: "Papo de bar", text: "Nome brasileiro na lista e a rua inteira para pra assistir" },
  { tag: "Cartolagem", text: "Multa rescisória assusta a diretoria e anima o empresário" },
  { tag: "Amarelinha", text: "Convocação sai e o grupo da família não aguenta o tranco" },
  { tag: "Departamento médico", text: "Fisioterapeuta pede calma; arquibancada pede o contrário" },
  { tag: "Janela aberta", text: "Proposta de fora do país chega e a diretoria some do telefone" },
  { tag: "Vestiário", text: "Capitão promete título e a torcida cobra por escrito" },
  { tag: "Tabela apertada", text: "Clube cai de divisão e descobre quem era torcedor de verdade" },
  { tag: "Festa na rua", text: "Time sobe, cidade fecha, e ninguém trabalha na segunda-feira" },
];
