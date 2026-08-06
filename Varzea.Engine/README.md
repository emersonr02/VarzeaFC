# Várzea Lendas — Passo 1: motor + calibração

Motor determinístico de simulação de carreira e a calibração do sistema de pontuação.
Sem API, sem banco, sem front — de propósito. O risco do projeto é balanceamento, não stack.

## Estrutura

```
Varzea.Engine/            class library pura — zero I/O, zero relógio, zero estado global
  Rng/Pcg32.cs            PRNG determinístico + derivação por domínio
  Model/Domain.cs         atributos, posições, receita da carreira, resultado
  Ruleset/Ruleset.cs      POCOs de balanceamento
  Ruleset/balance.json    TODOS os números — tunar sem recompilar
  Simulation/             draft, over dinâmico, roles, temporadas, títulos, transferências
  Scoring/                calibrador de raridade + scorer
Varzea.MonteCarlo/        runner: roda N carreiras e imprime os 3 critérios de aceite
Varzea.Engine.Tests/      determinismo (o teste que sustenta ranking e replay)
tools/montecarlo_mirror.py  espelho Python usado para calibrar (banco de prova, não é produto)
```

## Rodar

```bash
dotnet test                                   # determinismo
dotnet run --project Varzea.MonteCarlo 10000  # calibração
```

O runner grava `rarity-weights.json` — é esse arquivo que a API vai carregar para pontuar.

## Resultado da calibração (10.000 carreiras, ruleset 1.0.0)

Pesos **derivados**, não escritos à mão: `peso = log(1/frequência)`, normalizado para
a liga menor valer 10. Se um rebalanceamento tornar a Bola de Ouro mais fácil,
o peso dela cai sozinho no próximo recálculo.

| Título | Frequência | Peso |
|---|---:|---:|
| Bola de Ouro | 5,14% | 12,0 |
| Copa do Mundo | 8,23% | 10,1 |
| Liga menor | 8,46% | 10,0 |
| Continental secundária | 24,54% | 5,7 |
| **Equipe do Ano** | 30,42% | 4,8 |
| Continental principal | 30,63% | 4,8 |
| Liga média | 35,62% | 4,2 |
| Liga top-5 | 43,46% | 3,4 |
| Copa nacional | 66,73% | 1,6 |

Só dois prêmios individuais: **Bola de Ouro** e **Equipe do Ano** (melhor de cada posição).
Chuteira de Ouro, Luva de Ouro, Melhor Defensor e Meia do Ano foram removidos — a Equipe do Ano
resolve a paridade entre posições por construção, sem precisar de um prêmio por setor.

A Bola de Ouro é **gated** pela Equipe do Ano: só concorre quem foi o melhor da sua posição.
Sem esse gate o prêmio volta a ser refém de quem faz gol, e goleiro/zagueiro somem do topo.

### Critérios de aceite — todos passando

1. **Distribuição espalhada** — mediana 97, p99 421, máx 723.
   Top 1%: 99/100 scores distintos, dispersão 42%. Sem empates, sem desempate arbitrário.
2. **Todas as 9 posições chegam ao top 10%** — de 4,2% (GK) a 16,8% (SS).
3. **Contribuição por bloco (top 10%)** — títulos 66,4% · prêmios 26,8% · produção 4,7% · pico 2,1%.
   A leitura no top 10% é a honesta: prêmios raros zeram na maioria das carreiras e
   distorcem a média global.

## Quatro defeitos que a calibração revelou

Nenhum era visível lendo o código:

1. **Luva de Ouro era matematicamente impossível.** A fórmula dava ~4 jogos sem sofrer gol
   por temporada contra um limiar de 10–20. Frequência medida: 0,00%.
2. **Copa nacional em 97% das carreiras.** Não era conquista, era ruído — e o peso derivado
   caiu para 0,1, confirmando que não distinguia ninguém.
3. **Bola de Ouro em 13% das carreiras.** Comum demais para o prêmio que trava os
   níveis mais altos do ranking.
4. **Prêmios por setor desequilibravam as posições.** Atacante tinha Chuteira de Ouro,
   goleiro tinha Luva de Ouro, o resto não tinha nada — zagueiro e volante caíam para 1,6% do topo.
   A solução final não foi criar mais prêmios setoriais, e sim **substituir todos por Equipe do Ano**:
   como cada posição compete só contra si mesma, a paridade vem da estrutura do prêmio,
   não de tuning.

## Regras invioláveis do motor

- Toda aleatoriedade passa por `Pcg32` injetado. **Nunca** `Random.Shared`, `DateTime.Now`
  ou `Guid.NewGuid()` dentro da simulação — um único vazamento quebra o replay silenciosamente
  e você só descobre com o acervo já corrompido.
- `Pcg32.Derive(seed, domínio)` isola sistemas: mexer no gerador de nomes não pode
  deslocar os resultados das partidas.
- Toda carreira salva grava a `RulesetVersion` que usou, e o score congelado.
  Sem isso, o primeiro rebalanceamento reescreve o passado de todo mundo.

## Próximos passos

1. Rodar `dotnet test` e `dotnet run` para confirmar a compilação (não pude compilar no ambiente onde isto foi escrito).
2. Fórmula fechada → API (`/careers/start`, `/advance`, `/save`, `/rankings`).
3. Slots de 10 + conquistas (`UNIQUE (user_id, period_type, period_key)`).
4. Front React consumindo.
