// Run a copy beside a node_modules junction to Codex's bundled artifact-tool.
// From the repository root: node <copy.mjs> --inspect | --merge | --verify
import fs from 'node:fs/promises';
import path from 'node:path';
import { FileBlob, SpreadsheetFile } from '@oai/artifact-tool';

const root = process.cwd();
const dir = path.join(root, 'outputs/graveyard-merge-20261119');
const mode = process.argv[2] || '--inspect';
const filename = '讨伐队经理-图鉴（已合并低语墓园）.xlsx';
const input = mode === '--verify' ? path.join(dir, filename) : path.join(dir, 'source.xlsx');
const wb = await SpreadsheetFile.importXlsx(await FileBlob.load(input));
const sheet = wb.worksheets.getItem('敌人图鉴');

async function render(range, name) {
  const png = await wb.render({sheetName:'敌人图鉴',range,scale:1.4,format:'png'});
  await fs.writeFile(path.join(dir,name),new Uint8Array(await png.arrayBuffer()));
}

if (mode === '--inspect') {
  console.log((await wb.inspect({kind:'sheet',include:'id,name',maxChars:2000})).ndjson);
  console.log((await wb.inspect({kind:'computedStyle',sheetId:'敌人图鉴',range:'J23:O24',maxChars:2600})).ndjson);
  await render('A20:O24','source-enemies.png');
  // Baseline values/formulas let the merge verify every existing sheet in memory.
  const baseline = [];
  for (let i=0;i<8;i++) {
    const s=wb.worksheets.getItemAt(i), range=s.getUsedRange();
    baseline.push({name:s.name,address:range.address,values:range.values,formulas:range.formulas});
  }
  await fs.writeFile(path.join(dir,'baseline.json'),JSON.stringify(baseline));
  process.exit(0);
}

const baseline=JSON.parse(await fs.readFile(path.join(dir,'baseline.json'),'utf8'));
const data=JSON.parse(await fs.readFile(path.join(root,'Docs/WhisperingGraveyard.json'),'utf8'));
const appearance=JSON.parse(await fs.readFile(path.join(dir,'appearance.json'),'utf8'));
const ranks={Normal:'普通',Elite:'精英',Boss:'BOSS'};
const professions={Warrior:'战士',Mage:'法师',Archer:'射手',Support:'后勤',Monster:'怪物'};
const targets={Front:'最前方存活对手',Rear:'最后方存活对手',LowestHealth:'生命比例最低的存活对手',HighestAttack:'攻击最高的存活对手',CursedOrLowestDefense:'优先诅咒，同等情况下选防御最低者',LowestDefense:'防御最低的存活对手',Self:'自身',WoundedAlly:'生命比例最低的存活友方',FrontTwo:'最前两名存活对手',RearTwo:'最后两名存活对手',AllEnemies:'所有存活对手',AllAllies:'所有存活友方',StrongestAlly:'攻击最高的存活友方'};
const statuses={Vigor:'盛气',Swift:'迅捷',Resolve:'坚毅',Ember:'余烬',Poison:'中毒',Exposed:'破绽',Weakened:'萎靡',Stunned:'眩晕',Injured:'受伤',Cursed:'诅咒',Chill:'寒冷'};
const damage={Physical:'物理',Magic:'魔法',Fire:'火焰'};

function describe(a) {
  let text;
  if(a.kind==='Attack') {
    text=`${a.name}：${damage[a.damage||'Physical']}攻击${targets[a.target]}`;
    if(a.power) text+=`，攻击修正${a.power>0?'+':''}${a.power}`;
    if(a.status) text+=`；命中且目标存活后附加${a.layers}层${statuses[a.status]}`;
  } else if(a.kind==='Heal') text=`${a.name}：为${targets[a.target]}恢复${a.power}点生命`;
  else if(a.kind==='Shield') text=`${a.name}：为${targets[a.target]}增加${a.power}点护盾`;
  else text=`${a.name}：对${targets[a.target]}施加${a.layers}层${statuses[a.status]}`;
  if(a.charge) text+='；本次蓄力，下次可行动时释放并重选目标';
  return text+'。';
}

function extra(e) {
  const rules=[];
  if(e.openingShield) rules.push(`开战获得${e.openingShield}点护盾，每场一次。`);
  if(e.phaseThreshold) rules.push(`生命≤${e.phaseThreshold}%后首次可行动时，伤害招式攻击修正永久+${e.phasePower}，每场一次。`);
  if(e.actions.some(a=>a.kind==='Heal')) rules.push('治疗包含自身，排除倒下、满血或被诅咒的友方；无合法治疗目标时普攻。');
  if(e.actions.some(a=>a.delayDecay)) rules.push('诅咒、萎靡或眩晕首次附加时跳过第一次回合末递减。');
  if(e.actions.some(a=>a.kind==='Shield')) rules.push('护盾可保护满血单位，按招式固定数值累加。');
  if(e.actions.some(a=>a.charge)) rules.push('眩晕推迟蓄力释放。');
  rules.push(e.counter);
  return rules.join('\n');
}

if (mode === '--merge') {
  if(sheet.getRange('A2:A24').values.map(row=>row[0]).join(',') !== Array.from({length:23},(_,i)=>i+1).join(','))
    throw Error('The supplied roster differs from the reviewed 23-enemy baseline.');
  // Retain A:O and add the fields needed for a third action, regional sorting and portraits.
  for(const col of ['P','Q','R','S','T']) sheet.getRange(`${col}1`).copyFrom(sheet.getRange('O1'),'all');
  sheet.getRange('P1:T1').values=[['行为3','行为3概率','所属区域','占格','立绘']];
  for(const [col,width] of Object.entries({P:30,Q:12,R:15,S:8,T:18})) sheet.getRange(`${col}1:${col}54`).format.columnWidth=width;
  for(let row=2;row<=24;row++) {
    sheet.getRange(`R${row}:S${row}`).copyFrom(sheet.getRange('H24:I24'),'all');
    const rank=sheet.getRange(`C${row}`).values[0][0];
    sheet.getRange(`R${row}:S${row}`).values=[['弥雾码头',rank==='BOSS'?3:rank==='精英'?2:1]];
  }
  for(let i=0;i<data.enemies.length;i++) {
    const e=data.enemies[i], row=25+i;
    sheet.getRange(`A${row}:O${row}`).copyFrom(sheet.getRange('A24:O24'),'all');
    sheet.getRange(`P${row}`).copyFrom(sheet.getRange('L24'),'all');
    sheet.getRange(`Q${row}`).copyFrom(sheet.getRange('M24'),'all');
    sheet.getRange(`R${row}:T${row}`).copyFrom(sheet.getRange('G24:I24'),'all');
    const a=e.actions;
    sheet.getRange(`A${row}:T${row}`).values=[[e.number,e.name,ranks[e.rank],professions[e.profession],e.reach==='Melee'?'近战':'远程',e.attack,e.defense,e.specialDefense,e.health,
      describe(a[0]),a[0].weight/100,describe(a[1]),a[1].weight/100,extra(e),appearance[e.id],
      a[2]?describe(a[2]):null,a[2]?a[2].weight/100:null,'低语墓园',e.slots,null]];
    sheet.getRange(`A${row}:T${row}`).format.rowHeight=128;
    for(const col of ['J','L','N','O','P']) sheet.getRange(`${col}${row}`).format.wrapText=true;
    const bytes=await fs.readFile(path.join(root,`Assets/RaidPartyManager/Art/Enemies/WhisperingGraveyard/${e.id}-Portrait.png`));
    sheet.images.add({dataUrl:'data:image/png;base64,'+bytes.toString('base64'),anchor:{from:{row:row-1,col:19,rowOffsetPx:8,colOffsetPx:16},extent:{widthPx:100,heightPx:150}}});
  }
  for(const col of ['K','M','Q']) sheet.getRange(`${col}25:${col}54`).setNumberFormat('0%');
  sheet.getRange('P25:P54').format.verticalAlignment='center';
  await wb.recalculate();
}

// A narrow merge must preserve all original values/formulas, including sound planning.
for(const entry of baseline) {
  const current=wb.worksheets.getItem(entry.name).getRange(entry.address);
  if(JSON.stringify(current.values)!==JSON.stringify(entry.values) || JSON.stringify(current.formulas)!==JSON.stringify(entry.formulas))
    throw Error('Original values or formulas changed in '+entry.name);
}
const added=sheet.getRange('A25:T54').values;
if(added.length!==30 || added.some((r,i)=>r[0]!==data.enemies[i].number || r[1]!==data.enemies[i].name || Math.abs(r[10]+r[12]+(r[16]||0)-1)>1e-9))
  throw Error('New enemy IDs, names or action probabilities differ.');
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!|#NULL!|#SPILL!|#CALC!',options:{useRegex:true,maxResults:20},summary:'merged workbook formula errors'})).ndjson);
if (mode === '--merge') {
  await render('A24:I28','merged-stats.png');
  await render('J25:Q27','merged-actions.png');
  await render('N53:T54','merged-bosses.png');
  const result=await SpreadsheetFile.exportXlsx(wb);
  await result.save(path.join(dir,filename));
} else if (mode === '--verify') {
  await render('N53:T54','saved-bosses.png');
}
console.log(JSON.stringify({mode,originalSheets:8,totalEnemies:53,newEnemies:30,newActions:62,portraits:30,originalValuesAndFormulas:'unchanged',path:path.join(dir,filename)}));
