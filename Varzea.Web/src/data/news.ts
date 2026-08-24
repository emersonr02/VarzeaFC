// Manchetes do ticker da home — puro sabor, nenhuma influencia no motor. Tom da casa
// (várzea brasileira, deboche leve), não o de nenhum outro site: são frases próprias.
// A lista é fixa de propósito: é vitrine da home, não notícia da carreira do jogador
// (essa quem faz é o SeasonNewsFlash, com dados reais da temporada).
export interface Headline {
  tag: string;
  text: string;
}

export const HEADLINES: Headline[] = [
  { tag: "Última hora", text: "Moleque da várzea recusa peneira e diz que só sai por time grande" },
  { tag: "Mercado", text: "Empresário jura que o garoto “é o novo camisa 10 da seleção”" },
  { tag: "Oficial", text: "Mais um craque de fim de semana descobre o que é pré-temporada" },
  { tag: "Bastidores", text: "Olheiro europeu é visto comendo pastel na beira do campo" },
  { tag: "Bola de Ouro", text: "Brasileiro aparece na lista e a rua inteira para pra assistir" },
  { tag: "Contrato", text: "Multa rescisória assusta cartola e anima o empresário" },
  { tag: "Seleção", text: "Convocação sai e o grupo da família não aguenta o tranco" },
  { tag: "Lesão", text: "Departamento médico pede calma; torcida pede o contrário" },
  { tag: "Transferência", text: "Proposta de fora do país chega e a diretoria some do telefone" },
  { tag: "Vestiário", text: "Capitão promete título e a torcida cobra por escrito" },
  { tag: "Rebaixamento", text: "Clube cai de divisão e descobre quem era torcedor de verdade" },
  { tag: "Acesso", text: "Time sobe, cidade fecha, e ninguém trabalha na segunda-feira" },
];
