# Inventaire matériel

> Généré par `FanOMax.Probe inventory` le 2026-10-07 11:36. Lecture seule : aucune écriture PWM.

## Environnement

- Droits administrateur : oui
- Driver PawnIO : oui
- FanControl en cours d'exécution : oui (les % PWM ci-dessous sont alors ceux qu'il impose)

## Capteurs clés retenus

| Rôle | Capteur | Identifiant | Valeur |
|---|---|---|---|
| CpuTemperature | AMD Ryzen 7 5800X / Core (Tctl/Tdie) | `/amdcpu/0/temperature/2` | 69.6 °C |
| CpuPower | AMD Ryzen 7 5800X / Package | `/amdcpu/0/power/0` | 86.9 W |
| CpuLoad | AMD Ryzen 7 5800X / CPU Total | `/amdcpu/0/load/0` | 30.5 % |
| GpuTemperature | AMD Radeon RX 6750 XT / GPU Core | `/gpu-amd/0/temperature/0` | 49 °C |
| GpuHotSpot | AMD Radeon RX 6750 XT / GPU Hot Spot | `/gpu-amd/0/temperature/7` | 63 °C |
| GpuPower | AMD Radeon RX 6750 XT / GPU Package | `/gpu-amd/0/power/3` | 150 W |
| GpuLoad | AMD Radeon RX 6750 XT / GPU Core | `/gpu-amd/0/load/0` | 89 % |

## Contrôles de ventilateurs (PWM)

| Contrôle | Matériel | Mode | % actuel | Plage | Ventilateur associé (probable) | RPM |
|---|---|---|---|---|---|---|
| Fan #1 `/lpc/nct6796dr/0/control/0` | Nuvoton NCT6796D-R | Undefined | 52.2 | 0–100 | `/lpc/nct6796dr/0/fan/0` | 1718 |
| Fan #2 `/lpc/nct6796dr/0/control/1` | Nuvoton NCT6796D-R | Undefined | 47.8 | 0–100 | `/lpc/nct6796dr/0/fan/1` | 898 |
| Fan #3 `/lpc/nct6796dr/0/control/2` | Nuvoton NCT6796D-R | Undefined | 69.8 | 0–100 | `/lpc/nct6796dr/0/fan/2` | 0 |
| Fan #4 `/lpc/nct6796dr/0/control/3` | Nuvoton NCT6796D-R | Undefined | 56.9 | 0–100 | `/lpc/nct6796dr/0/fan/3` | 0 |
| Fan #5 `/lpc/nct6796dr/0/control/4` | Nuvoton NCT6796D-R | Undefined | 47.8 | 0–100 | `/lpc/nct6796dr/0/fan/4` | 0 |
| Fan #6 `/lpc/nct6796dr/0/control/5` | Nuvoton NCT6796D-R | Undefined | 30.2 | 0–100 | `/lpc/nct6796dr/0/fan/5` | 0 |
| Fan #7 `/lpc/nct6796dr/0/control/6` | Nuvoton NCT6796D-R | Undefined | 52.2 | 0–100 | `/lpc/nct6796dr/0/fan/6` | 843 |
| GPU Fan `/gpu-amd/0/control/0` | AMD Radeon RX 6750 XT | Undefined | 78 | 0–100 | `/gpu-amd/0/fan/0` | 2850 |

> L'association contrôle ↔ ventilateur est déduite de l'index du canal. Elle sera confirmée en phase 7 (variation d'un PWM et observation du RPM).

## Détail par matériel

### ASRock B550 Pro4 (Motherboard)

Identifiant : `/motherboard`

#### Nuvoton NCT6796D-R (SuperIO)

Identifiant : `/lpc/nct6796dr/0`

| Type | Nom | Identifiant | Valeur | Min | Max |
|---|---|---|---|---|---|
| Temperature | Temperature #1 | `/lpc/nct6796dr/0/temperature/1` | 48 °C | 48 | 48 |
| Temperature | Temperature #2 | `/lpc/nct6796dr/0/temperature/2` | 35 °C | 35 | 35 |
| Temperature | Temperature #3 | `/lpc/nct6796dr/0/temperature/3` | 8 °C | 8 | 8 |
| Temperature | Temperature #5 | `/lpc/nct6796dr/0/temperature/5` | 12 °C | 12 | 12 |
| Temperature | Temperature #6 | `/lpc/nct6796dr/0/temperature/6` | 31 °C | 31 | 31 |
| Fan | Fan #1 | `/lpc/nct6796dr/0/fan/0` | 1717.6 RPM | 1717.6 | 1757.8 |
| Fan | Fan #2 | `/lpc/nct6796dr/0/fan/1` | 897.6 RPM | 897.6 | 910.3 |
| Fan | Fan #3 | `/lpc/nct6796dr/0/fan/2` | 0 RPM | 0 | 0 |
| Fan | Fan #4 | `/lpc/nct6796dr/0/fan/3` | 0 RPM | 0 | 0 |
| Fan | Fan #5 | `/lpc/nct6796dr/0/fan/4` | 0 RPM | 0 | 0 |
| Fan | Fan #6 | `/lpc/nct6796dr/0/fan/5` | 0 RPM | 0 | 0 |
| Fan | Fan #7 | `/lpc/nct6796dr/0/fan/6` | 843.2 RPM | 830.3 | 843.2 |
| Control | Fan #1 | `/lpc/nct6796dr/0/control/0` | 52.2 % | 52.2 | 52.2 |
| Control | Fan #2 | `/lpc/nct6796dr/0/control/1` | 47.8 % | 47.8 | 47.8 |
| Control | Fan #3 | `/lpc/nct6796dr/0/control/2` | 69.8 % | 69.8 | 69.8 |
| Control | Fan #4 | `/lpc/nct6796dr/0/control/3` | 56.9 % | 56.9 | 56.9 |
| Control | Fan #5 | `/lpc/nct6796dr/0/control/4` | 47.8 % | 47.8 | 47.8 |
| Control | Fan #6 | `/lpc/nct6796dr/0/control/5` | 30.2 % | 30.2 | 30.2 |
| Control | Fan #7 | `/lpc/nct6796dr/0/control/6` | 52.2 % | 52.2 | 52.2 |
| Voltage | Vcore | `/lpc/nct6796dr/0/voltage/0` | 2.8 V | 2.8 | 2.8 |
| Voltage | Voltage #2 | `/lpc/nct6796dr/0/voltage/1` | 1.7 V | 1.7 | 1.7 |
| Voltage | Voltage #11 | `/lpc/nct6796dr/0/voltage/10` | 1 V | 1 | 1 |
| Voltage | Voltage #12 | `/lpc/nct6796dr/0/voltage/11` | 0.6 V | 0.6 | 0.6 |
| Voltage | Voltage #13 | `/lpc/nct6796dr/0/voltage/12` | 1.1 V | 1.1 | 1.1 |
| Voltage | Voltage #14 | `/lpc/nct6796dr/0/voltage/13` | 0.9 V | 0.9 | 0.9 |
| Voltage | Voltage #15 | `/lpc/nct6796dr/0/voltage/14` | 0.9 V | 0.9 | 0.9 |
| Voltage | AVCC | `/lpc/nct6796dr/0/voltage/2` | 3.4 V | 3.4 | 3.4 |
| Voltage | +3.3V | `/lpc/nct6796dr/0/voltage/3` | 3.3 V | 3.3 | 3.3 |
| Voltage | Voltage #5 | `/lpc/nct6796dr/0/voltage/4` | 1.8 V | 1.8 | 1.8 |
| Voltage | Voltage #6 | `/lpc/nct6796dr/0/voltage/5` | 1 V | 1 | 1 |
| Voltage | Voltage #7 | `/lpc/nct6796dr/0/voltage/6` | 1.3 V | 1.3 | 1.3 |
| Voltage | +3V Standby | `/lpc/nct6796dr/0/voltage/7` | 3.4 V | 3.4 | 3.4 |
| Voltage | CPU Termination | `/lpc/nct6796dr/0/voltage/9` | 0.9 V | 0.9 | 0.9 |

### AMD Ryzen 7 5800X (Cpu)

Identifiant : `/amdcpu/0`

| Type | Nom | Identifiant | Valeur | Min | Max |
|---|---|---|---|---|---|
| Other | Core #1 | `/amdcpu/0/factor/0` | 48.3  | 48.3 | 48.5 |
| Other | Core #2 | `/amdcpu/0/factor/1` | 48.3  | 48.3 | 48.5 |
| Other | Core #3 | `/amdcpu/0/factor/2` | 48.3  | 48.3 | 48.5 |
| Other | Core #4 | `/amdcpu/0/factor/3` | 48.5  | 38.8 | 48.5 |
| Other | Core #5 | `/amdcpu/0/factor/4` | 38.8  | 38.8 | 38.8 |
| Other | Core #6 | `/amdcpu/0/factor/5` | 48.5  | 48.5 | 48.5 |
| Other | Core #7 | `/amdcpu/0/factor/6` | 38.8  | 38.8 | 48.5 |
| Other | Core #8 | `/amdcpu/0/factor/7` | 48.3  | 48.3 | 48.5 |
| Temperature | Core (Tctl/Tdie) | `/amdcpu/0/temperature/2` | 69.6 °C | 69.5 | 69.6 |
| Temperature | CCD1 (Tdie) | `/amdcpu/0/temperature/3` | 67.5 °C | 66.3 | 67.8 |
| Power | Package | `/amdcpu/0/power/0` | 86.9 W | 86.9 | 88.6 |
| Power | Core #1 (SMU) | `/amdcpu/0/power/1` | 11.2 W | 11.2 | 12.4 |
| Power | Core #2 (SMU) | `/amdcpu/0/power/2` | 8.8 W | 8.8 | 9.2 |
| Power | Core #3 (SMU) | `/amdcpu/0/power/3` | 10.7 W | 10.7 | 11 |
| Power | Core #4 (SMU) | `/amdcpu/0/power/4` | 7.1 W | 7.1 | 7.4 |
| Power | Core #5 (SMU) | `/amdcpu/0/power/5` | 5.5 W | 5.5 | 5.7 |
| Power | Core #6 (SMU) | `/amdcpu/0/power/6` | 6.2 W | 6.2 | 6.5 |
| Power | Core #7 (SMU) | `/amdcpu/0/power/7` | 5.8 W | 5.8 | 5.9 |
| Power | Core #8 (SMU) | `/amdcpu/0/power/8` | 7.6 W | 7.6 | 8.1 |
| Load | CPU Total | `/amdcpu/0/load/0` | 30.5 % | 30.5 | 37.9 |
| Load | CPU Core Max | `/amdcpu/0/load/1` | 62.1 % | 62.1 | 100 |
| Load | CPU Core #9 | `/amdcpu/0/load/10` | 16.7 % | 11.1 | 16.7 |
| Load | CPU Core #10 | `/amdcpu/0/load/11` | 12.1 % | 0 | 12.1 |
| Load | CPU Core #11 | `/amdcpu/0/load/12` | 16.7 % | 16.7 | 44.4 |
| Load | CPU Core #12 | `/amdcpu/0/load/13` | 27.3 % | 27.3 | 40 |
| Load | CPU Core #13 | `/amdcpu/0/load/14` | 13.6 % | 13.6 | 33.3 |
| Load | CPU Core #14 | `/amdcpu/0/load/15` | 28.8 % | 22.2 | 28.8 |
| Load | CPU Core #15 | `/amdcpu/0/load/16` | 27.3 % | 27.3 | 55.6 |
| Load | CPU Core #16 | `/amdcpu/0/load/17` | 27.3 % | 27.3 | 33.3 |
| Load | CPU Core #1 | `/amdcpu/0/load/2` | 42.4 % | 42.4 | 100 |
| Load | CPU Core #2 | `/amdcpu/0/load/3` | 62.1 % | 11.1 | 71.4 |
| Load | CPU Core #3 | `/amdcpu/0/load/4` | 39.4 % | 39.4 | 45.2 |
| Load | CPU Core #4 | `/amdcpu/0/load/5` | 37.9 % | 37.9 | 40 |
| Load | CPU Core #5 | `/amdcpu/0/load/6` | 34.9 % | 33.3 | 42.9 |
| Load | CPU Core #6 | `/amdcpu/0/load/7` | 53 % | 46.3 | 60 |
| Load | CPU Core #7 | `/amdcpu/0/load/8` | 22.7 % | 22.2 | 23.8 |
| Load | CPU Core #8 | `/amdcpu/0/load/9` | 25.8 % | 25.8 | 55.6 |
| Clock | Bus Speed | `/amdcpu/0/clock/0` | 99.8 MHz | 99.8 | 99.8 |
| Clock | Cores (Average) | `/amdcpu/0/clock/1` | 4586 MHz | 0 | 4599 |
| Clock | Core #4 (Effective) | `/amdcpu/0/clock/10` | 1276.5 MHz | 0 | 1461.5 |
| Clock | Core #5 | `/amdcpu/0/clock/11` | 3873 MHz | 3873 | 3873 |
| Clock | Core #5 (Effective) | `/amdcpu/0/clock/12` | 715.5 MHz | 0 | 815.5 |
| Clock | Core #6 | `/amdcpu/0/clock/13` | 4841 MHz | 4841 | 4841 |
| Clock | Core #6 (Effective) | `/amdcpu/0/clock/14` | 998.5 MHz | 0 | 1177.5 |
| Clock | Core #7 | `/amdcpu/0/clock/15` | 3873 MHz | 3873 | 4841 |
| Clock | Core #7 (Effective) | `/amdcpu/0/clock/16` | 875.5 MHz | 0 | 992.5 |
| Clock | Core #8 | `/amdcpu/0/clock/17` | 4816 MHz | 4816 | 4841 |
| Clock | Core #8 (Effective) | `/amdcpu/0/clock/18` | 1540 MHz | 0 | 1789 |
| Clock | Cores (Average Effective) | `/amdcpu/0/clock/2` | 1569 MHz | 0 | 1747 |
| Clock | Core #1 | `/amdcpu/0/clock/3` | 4816 MHz | 4816 | 4841 |
| Clock | Core #1 (Effective) | `/amdcpu/0/clock/4` | 2727.5 MHz | 0 | 3030.5 |
| Clock | Core #2 | `/amdcpu/0/clock/5` | 4816 MHz | 4816 | 4841 |
| Clock | Core #2 (Effective) | `/amdcpu/0/clock/6` | 1978.5 MHz | 0 | 2176.5 |
| Clock | Core #3 | `/amdcpu/0/clock/7` | 4816 MHz | 4816 | 4841 |
| Clock | Core #3 (Effective) | `/amdcpu/0/clock/8` | 2442.5 MHz | 0 | 2532.5 |
| Clock | Core #4 | `/amdcpu/0/clock/9` | 4841 MHz | 3873 | 4841 |
| Voltage | Core (SVI2 TFN) | `/amdcpu/0/voltage/0` | 1.3 V | 1.3 | 1.4 |
| Voltage | SoC (SVI2 TFN) | `/amdcpu/0/voltage/1` | 1 V | 1 | 1 |
| Voltage | Core #1 VID | `/amdcpu/0/voltage/2` | 1.3 V | 1.3 | 1.4 |
| Voltage | Core #2 VID | `/amdcpu/0/voltage/3` | 1.4 V | 1.4 | 1.4 |
| Voltage | Core #3 VID | `/amdcpu/0/voltage/4` | 1.4 V | 1.4 | 1.4 |
| Voltage | Core #4 VID | `/amdcpu/0/voltage/5` | 1.4 V | 1.4 | 1.4 |
| Voltage | Core #5 VID | `/amdcpu/0/voltage/6` | 1.4 V | 1.3 | 1.4 |
| Voltage | Core #6 VID | `/amdcpu/0/voltage/7` | 1.4 V | 1.3 | 1.4 |
| Voltage | Core #7 VID | `/amdcpu/0/voltage/8` | 1.4 V | 1.3 | 1.4 |
| Voltage | Core #8 VID | `/amdcpu/0/voltage/9` | 1.4 V | 1.3 | 1.4 |

### AMD Radeon RX 6750 XT (GpuAmd)

Identifiant : `/gpu-amd/0`

| Type | Nom | Identifiant | Valeur | Min | Max |
|---|---|---|---|---|---|
| Other | Fullscreen FPS | `/gpu-amd/0/factor/0` | 0  | -1 | 0 |
| Other | GPU Memory Used | `/gpu-amd/0/smalldata/0` | 4192  | 4190 | 4192 |
| Other | GPU Memory Free | `/gpu-amd/0/smalldata/1` | 8080  | 8080 | 8082 |
| Other | GPU Memory Total | `/gpu-amd/0/smalldata/2` | 12272  | 12272 | 12272 |
| Other | D3D Dedicated Memory Used | `/gpu-amd/0/smalldata/3` | 4192.3  | 4189.3 | 4192.3 |
| Other | D3D Dedicated Memory Free | `/gpu-amd/0/smalldata/4` | 8049.9  | 8049.9 | 8052.9 |
| Other | D3D Dedicated Memory Total | `/gpu-amd/0/smalldata/5` | 12242.2  | 12242.2 | 12242.2 |
| Other | D3D Shared Memory Used | `/gpu-amd/0/smalldata/6` | 666.2  | 666.2 | 666.2 |
| Other | D3D Shared Memory Free | `/gpu-amd/0/smalldata/7` | 15680.3  | 15680.3 | 15680.3 |
| Other | D3D Shared Memory Total | `/gpu-amd/0/smalldata/8` | 16346.4  | 16346.4 | 16346.4 |
| Temperature | GPU Core | `/gpu-amd/0/temperature/0` | 49 °C | 48 | 49 |
| Temperature | GPU Hot Spot | `/gpu-amd/0/temperature/7` | 63 °C | 60 | 63 |
| Power | GPU Package | `/gpu-amd/0/power/3` | 150 W | 0 | 150 |
| Load | GPU Core | `/gpu-amd/0/load/0` | 89 % | 70 | 89 |
| Load | GPU Memory | `/gpu-amd/0/load/1` | 7 % | 5 | 7 |
| Load | D3D Security 1 | `/gpu-amd/0/load/10` | 0 % | 0 | 0 |
| Load | D3D Timer 0 | `/gpu-amd/0/load/11` | 0 % | 0 | 0 |
| Load | D3D True Audio 0 | `/gpu-amd/0/load/12` | 0 % | 0 | 0 |
| Load | D3D True Audio 1 | `/gpu-amd/0/load/13` | 0 % | 0 | 0 |
| Load | D3D Video Codec 0 | `/gpu-amd/0/load/14` | 0 % | 0 | 0 |
| Load | D3D Video Decode 1 | `/gpu-amd/0/load/15` | 0 % | 0 | 0 |
| Load | D3D Video JPEG 0 | `/gpu-amd/0/load/16` | 0 % | 0 | 0 |
| Load | D3D 3D | `/gpu-amd/0/load/2` | 89.6 % | 57.6 | 89.6 |
| Load | D3D Compute 0 | `/gpu-amd/0/load/3` | 0 % | 0 | 0 |
| Load | D3D Compute 1 | `/gpu-amd/0/load/4` | 0 % | 0 | 0 |
| Load | D3D Compute 3 | `/gpu-amd/0/load/5` | 0 % | 0 | 0 |
| Load | D3D Copy | `/gpu-amd/0/load/6` | 1.1 % | 0.9 | 1.1 |
| Load | D3D Copy | `/gpu-amd/0/load/7` | 13.1 % | 6.3 | 13.1 |
| Load | D3D High Priority 3D | `/gpu-amd/0/load/8` | 0 % | 0 | 0 |
| Load | D3D High Priority Compute | `/gpu-amd/0/load/9` | 0 % | 0 | 0 |
| Fan | GPU Fan | `/gpu-amd/0/fan/0` | 2850 RPM | 2845 | 2850 |
| Control | GPU Fan | `/gpu-amd/0/control/0` | 78 % | 78 | 78 |
| Clock | GPU Core | `/gpu-amd/0/clock/0` | 2543 MHz | 2508 | 2543 |
| Clock | GPU Memory | `/gpu-amd/0/clock/2` | 2238 MHz | 2238 | 2238 |
| Voltage | GPU Core | `/gpu-amd/0/voltage/0` | 1.1 V | 1.1 | 1.1 |

