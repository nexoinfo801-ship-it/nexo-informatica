# NEXO ERP PRO — BETA MACHINE LOCK V1

Status: PREP / LAB. Não representa EXE comercial homologado.

## Objetivo
Impedir que uma cópia simples da pasta do Cliente NEXO seja reutilizada em outro computador. A proteção não depende de senha de pasta.

## Camadas
1. Licença assinada NEXO_LICENSE V3 permanece autoridade comercial.
2. `install_id` deve coincidir com a licença/ativação.
3. `machine_binding` = SHA-256 com domain separation sobre `MachineGuid + install_id`.
4. A licença Beta deve conter política: `channel=BETA`, `max_devices=1`, `transfer_allowed=false`, `expires_at` válido.
5. Licença/status e registro local devem continuar protegidos pelo Electron `safeStorage` no runtime Windows.
6. Na divergência de binding: bloquear antes de abrir ERP/PDV e apresentar fluxo de transferência/reativação. Nunca tentar recalcular/aceitar silenciosamente outra máquina.
7. O Cliente nunca recebe chave privada MASTER/status.

## Integração na baseline Desktop
A baseline canônica já possui leitura do `MachineGuid` e `stableInstallId()` no `main.cjs`. A integração deve importar `client-security/machine-lock.cjs` para o processo principal e executar `evaluateMachineLock()` depois da verificação criptográfica da licença e antes da criação da janela principal.

Fluxo alvo:

`bootstrap -> integridade release -> identidade Windows -> licença assinada -> machine lock -> status remoto/local -> abrir BrowserWindow`

Se `MACHINE_BINDING_MISMATCH`, `LICENSE_INSTALL_ID_MISMATCH` ou `BETA_EXPIRED`: não criar janela operacional. Exibir somente tela segura de ativação/transferência.

## Emissão Beta
O MASTER deve emitir a licença para um `install_id` específico e incluir:

```json
{
  "install_id": "NXM-...",
  "machine_binding": "<sha256>",
  "beta_policy": {
    "channel": "BETA",
    "expires_at": "<ISO-8601>",
    "max_devices": 1,
    "transfer_allowed": false
  }
}
```

O `machine_binding` deve ser calculado no computador autorizado (ou a partir de um pedido de ativação autenticado) e então congelado na licença assinada. Não enviar `MachineGuid` bruto para logs/relatórios; usar apenas o hash domain-separated.

## Gate de teste
- mesma máquina + licença válida -> PASS;
- pasta copiada para outro PC -> BLOCK;
- `install_id` diferente -> BLOCK;
- Beta vencido -> BLOCK;
- transferência não autorizada -> BLOCK;
- registro local não contém chave de licença em claro -> PASS;
- safeStorage indisponível para segredo crítico -> FAIL-CLOSED;
- Windows real deve validar MachineGuid e safeStorage antes de liberar Beta externo.

## Limite
ASAR/obfuscação não são DRM. Eles aumentam resistência a alteração casual, mas a proteção principal é licença assinada + binding da máquina + integridade do release + chave privada fora do Cliente.
