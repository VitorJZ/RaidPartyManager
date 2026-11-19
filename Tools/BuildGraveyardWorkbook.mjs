// Execute a copy from outputs/graveyard-20261119/, with its node_modules junction
// pointing at load_workspace_dependencies' bundled packages. Run from repo root.
import fs from 'node:fs/promises';
import path from 'node:path';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';

const root = process.cwd();
const output = path.join(root, 'outputs/graveyard-20261119');
const data = JSON.parse(await fs.readFile(path.join(root, 'Docs/WhisperingGraveyard.json'), 'utf8'));
const enemies = data.enemies;
const wb = Workbook.create();
const roster = wb.worksheets.add('怪物图鉴');
const actions = wb.worksheets.add('行动图鉴');
const rules = wb.worksheets.add('规则与难度');
const ranks = { Normal: '普通', Elite: '精英', Boss: 'Boss' };
const professions = { Warrior: '战士', Mage: '法师', Archer: '射手', Support: '后勤', Monster: '怪物' };
const kinds = { Attack: '攻击', Status: '状态', Heal: '治疗', Shield: '护盾' };
const targets = { Front: '最前存活对手', Rear: '最后存活对手', LowestHealth: '生命比例最低的对手',
  HighestAttack: '攻击最高的对手', LowestDefense: '防御最低的对手', Self: '自身',
  WoundedAlly: '生命比例最低的存活友方', FrontTwo: '最前两名存活对手', RearTwo: '最后两名存活对手',
  AllEnemies: '所有存活对手', AllAllies: '所有存活友方', StrongestAlly: '攻击最高的存活友方' };
const statuses = { Vigor: '盛气', Swift: '迅捷', Resolve: '坚毅', Ember: '余烬', Poison: '中毒',
  Exposed: '破绽', Weakened: '萎靡', Stunned: '眩晕', Injured: '受伤', Cursed: '诅咒', Chill: '寒冷' };
const damage = { Physical: '物理', Magic: '魔法', Fire: '火焰' };
const lastActionRow = 7 + enemies.reduce((n, e) => n + e.actions.length, 0);

function base(sheet, title, columns, lastRow) {
  sheet.showGridLines = false;
  sheet.getRange(`A1:${columns}${lastRow}`).format.font.name = 'Microsoft YaHei';
  sheet.getRange(`A1:${columns}${lastRow}`).format.font.size = 10;
  sheet.getRange(`A1:${columns}${lastRow}`).format.font.color = '#242B38';
  sheet.getRange('A2').values = [[title]];
  sheet.getRange('A2').format.font.size = 16;
  sheet.getRange('A2').format.font.bold = true;
  sheet.getRange('A2').format.rowHeight = 26;
  sheet.getRange(`A7:${columns}7`).format = {
    fill: '#31364B', font: { bold: true, color: '#FFFFFF', size: 10 },
    rowHeight: 28, horizontalAlignment: 'center', verticalAlignment: 'center'
  };
  sheet.getRange(`A8:${columns}${lastRow}`).format.verticalAlignment = 'center';
  sheet.freezePanes.freezeRows(7);
}

base(roster, '低语墓园敌人图鉴', 'O', 37);
roster.getRange('A4').values = [['第 2 层区域。23 名普通敌人、5 名精英、2 名 Boss。2026-11-19。']];
roster.getRange('A5').values = [['行动概率、目标和效果见“行动图鉴”。数值为加强版初始配置。']];
roster.getRange('A7:O7').values = [['序号','立绘','名称','级别','职业','攻击方式','攻击','防御','特防','生命','占格','开场护盾','第二阶段','应对方法','概率合计']];
const values = enemies.map(e => [e.number,null,e.name,ranks[e.rank],professions[e.profession],e.reach==='Melee'?'近战':'远程',
  e.attack,e.defense,e.specialDefense,e.health,e.slots,e.openingShield||0,
  e.phaseThreshold?`生命≤${e.phaseThreshold}%，伤害招式修正+${e.phasePower}`:'无',e.counter,null]);
roster.getRange('A8:O37').values = values;
for (const [col,width] of Object.entries({A:7,B:14,C:20,D:9,E:9,F:10,G:7,H:7,I:7,J:8,K:7,L:11,M:28,N:66,O:11}))
  roster.getRange(`${col}7:${col}37`).format.columnWidth = width;
roster.getRange('A8:O37').format.rowHeight = 100;
roster.getRange('M8:N37').format.wrapText = true;
roster.getRange('G8:L37').format.horizontalAlignment = 'center';
for (let i=0;i<enemies.length;i++) {
  const row=i+8, e=enemies[i];
  if(i%2===1) roster.getRange(`A${row}:O${row}`).format.fill='#F3F4F7';
  roster.getRange(`O${row}`).formulas=[[`=SUMIF('行动图鉴'!$A$8:$A$${lastActionRow},A${row},'行动图鉴'!$F$8:$F$${lastActionRow})`]];
  const imagePath=path.join(root, 'Assets/RaidPartyManager/Art/Enemies/WhisperingGraveyard',`${e.id}-Portrait.png`);
  const bytes=await fs.readFile(imagePath);
  roster.images.add({dataUrl:'data:image/png;base64,'+bytes.toString('base64'),
    anchor:{from:{row:row-1,col:1,rowOffsetPx:5,colOffsetPx:10},extent:{widthPx:76,heightPx:114}}});
}
roster.getRange('O8:O37').setNumberFormat('0%');
roster.getRange('O8:O37').conditionalFormats.add('cellIs',{operator:'notEqual',formula:1,
  format:{fill:'#FBE2DF',font:{color:'#A12622',bold:true}}});
roster.tables.add('A7:O37',true,'GraveyardEnemies');

base(actions,'低语墓园行动图鉴','O',lastActionRow);
actions.getRange('A4').values=[['权重按每次自由行动抽取。蓄力动作先预警，下次可行动时必定释放，不再抽取其他行动。']];
actions.getRange('A5').values=[['治疗无受伤且可治疗的存活友方时改为普攻。目标并列按站位从前到后，群攻按站位逐个结算。']];
actions.getRange('A7:O7').values=[['怪物序号','怪物名称','行动序号','行动名称','权重','概率','类型','目标','伤害类型','攻击修正','治疗／护盾','状态','层数','生效时机','动作表现']];
const actionValues=[];
for (const e of enemies) for(let i=0;i<e.actions.length;i++) {
  const a=e.actions[i];
  actionValues.push([e.number,e.name,i+1,a.name,a.weight,null,kinds[a.kind],targets[a.target],
    a.kind==='Attack'?damage[a.damage||'Physical']:'无',a.kind==='Attack'?(a.power||0):null,
    ['Heal','Shield'].includes(a.kind)?a.power:null,a.status?statuses[a.status]:'无',a.layers||0,
    a.charge?'预警后，下次可行动时释放':a.kind==='Attack'&&a.status?'命中且目标存活后附加':'本次行动',
    i===0?'普攻第 5 帧生效':'特技第 5 帧生效']);
}
actions.getRange(`A8:O${lastActionRow}`).values=actionValues;
for(let row=8;row<=lastActionRow;row++) actions.getRange(`F${row}`).formulas=
  [[`=E${row}/SUMIF($A$8:$A$${lastActionRow},A${row},$E$8:$E$${lastActionRow})`]];
actions.getRange(`F8:F${lastActionRow}`).setNumberFormat('0%');
for (const [col,width] of Object.entries({A:10,B:20,C:10,D:20,E:7,F:8,G:9,H:32,I:10,J:11,K:13,L:10,M:8,N:32,O:24}))
  actions.getRange(`${col}7:${col}${lastActionRow}`).format.columnWidth=width;
actions.getRange(`A8:O${lastActionRow}`).format.rowHeight=30;
actions.tables.add(`A7:O${lastActionRow}`,true,'GraveyardActions');

base(rules,'低语墓园规则与难度','D',30);
rules.getRange('A4').values=[['设计依据为区域图鉴、码头敌人图鉴与现有自动战斗规则。墓园空缺的内容由本次设计补全。']];
rules.getRange('A7:D7').values=[['事项','规则','数值／条件','应对与说明']];
const ruleRows=[
  ['普通敌人','基础范围','攻击4–6，生命16–26','面向已有配装的二阶队伍。支援单位单次治疗5–6。'],
  ['精英','基础范围',`攻击${Math.min(...enemies.filter(e=>e.rank==='Elite').map(e=>e.attack))}–${Math.max(...enemies.filter(e=>e.rank==='Elite').map(e=>e.attack))}，生命${Math.min(...enemies.filter(e=>e.rank==='Elite').map(e=>e.health))}–${Math.max(...enemies.filter(e=>e.rank==='Elite').map(e=>e.health))}，占2格`,'承伤、治疗、群攻和寒冷各有专长。'],
  ['Boss','基础范围',enemies.filter(e=>e.rank==='Boss').map(e=>`${e.name}：攻击${e.attack}，生命${e.health}`).join('；'),'占3格。开场护盾属于独立护盾，不计入生命。'],
  ['第二阶段','半血时首次可行动','伤害招式攻击修正+1','只进入一次。治疗、护盾和状态层数不增加。'],
  ['攻击计算','沿用现有伤害链','攻击+招式修正，再进行防御与状态结算','无状态时伤害最低1。物理对应防御，魔法对应特防。'],
  ['蓄力','本次预警，下次可行动时释放','眩晕推迟释放','释放时重新选择目标，玩家回合间换位可影响命中者。'],
  ['治疗','选择生命比例最低的可治疗存活友方','包括自身，排除诅咒与倒下单位','没有合法目标时普攻。'],
  ['护盾','保护存活友方','可保护满血角色','护盾数值按招式固定值累加。'],
  ['状态持续','沿用状态图鉴','诅咒、萎靡、眩晕首次附加延后一次递减','避免敌方后行动造成状态在玩家行动前消失。'],
  ['普通遭遇','按出战人数匹配敌人数','最多4格','建议同场最多1个治疗者、1个眩晕来源。当前演练可自由混编。'],
  ['精英遭遇','一名精英和普通随从','总占格不超过4','建议1精英+1–2普通，不配双治疗。'],
  ['Boss遭遇','一名Boss和一名普通随从','3+1格','建议选择普通战士随从，避免高压控制叠加。'],
  ['美术与动作','透明像素绘制，朝左','待机8、移动6、普攻8、特技8、受击4、倒下8','原始生成图另保留受击8帧。回放速度不改变模拟结果。'],
  ['行动概率','表内为自由行动抽取概率','30名怪物的权重各合计100','蓄力占用两次行动，所以实际每回合释放率低于抽取概率。'],
  ['场景效果','低语墓园原策划留空','本次不增加强制环境伤害','本批强度来自敌人属性、站位和招式。']
];
rules.getRange(`A8:D${7+ruleRows.length}`).values=ruleRows;
for(const [col,width] of Object.entries({A:16,B:32,C:48,D:86})) rules.getRange(`${col}7:${col}30`).format.columnWidth=width;
rules.getRange('A8:D30').format.rowHeight=36;
rules.getRange('B8:D30').format.wrapText=true;
rules.tables.add(`A7:D${7+ruleRows.length}`,true,'GraveyardRules');

const balance = wb.worksheets.add('难度试算');
const csv = (await fs.readFile(path.join(root,'Logs/Graveyard/Balance.csv'),'utf8')).replace(/^\uFEFF/,'').trim().split(/\r?\n/).map(line=>line.split(','));
const balanceLastRow = 7 + csv.length - 1;
base(balance,'固定阵容 · 难度试算','H',balanceLastRow);
balance.getRange('A4').values=[['11组遭遇 × 2套队伍 × 种子0–99，共2200场。固定站位1–4，满生命，无已学技能。']];
balance.getRange('A5').values=[['一阶无配装；二阶八件装备配置见仓库GraveyardTests。胜率只代表这些固定配置，非全体构筑通关率。']];
balance.getRange('A7:H7').values=[csv[0]];
balance.getRange(`A8:H${balanceLastRow}`).values=csv.slice(1).map(row=>row.map((v,i)=>i<2||v==='n.a.'?v:Number(v)));
for(let row=8;row<=balanceLastRow;row++) balance.getRange(`E${row}`).formulas=[[`=D${row}/C${row}`]];
for(const [col,width] of Object.entries({A:23,B:23,C:11,D:11,E:12,F:14,G:24,H:26})) balance.getRange(`${col}7:${col}${balanceLastRow}`).format.columnWidth=width;
balance.getRange(`A8:H${balanceLastRow}`).format.rowHeight=28;
balance.getRange(`E8:E${balanceLastRow}`).setNumberFormat('0%');
balance.getRange(`H8:H${balanceLastRow}`).setNumberFormat('0.0%');
balance.getRange(`F8:G${balanceLastRow}`).setNumberFormat('0.00');
balance.tables.add(`A7:H${balanceLastRow}`,true,'GraveyardBalance');

await wb.recalculate();
const probabilityChecks=roster.getRange('O8:O37').values;
if(probabilityChecks.some(([v])=>Math.abs(v-1)>1e-9)) throw Error('Action probability totals do not equal 100%');
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!|#NULL!|#SPILL!|#CALC!',
  options:{useRegex:true,maxResults:20},summary:'final formula scan'})).ndjson);
await fs.mkdir(output,{recursive:true});
for(const [sheetName,range,file] of [['怪物图鉴','A7:L12','roster-preview.png'],['行动图鉴','A7:O13','actions-preview.png'],['规则与难度','A7:D15','rules-preview.png'],['难度试算',`A7:H${balanceLastRow}`,'balance-preview.png']]) {
  const preview=await wb.render({sheetName,range,scale:1.4,format:'png'});
  await fs.writeFile(path.join(output,file),new Uint8Array(await preview.arrayBuffer()));
}
const result=await SpreadsheetFile.exportXlsx(wb);
await result.save(path.join(output,'低语墓园图鉴.xlsx'));
console.log(JSON.stringify({enemies:enemies.length,actions:actionValues.length,probabilities:'all 100%',path:path.join(output,'低语墓园图鉴.xlsx')}));
