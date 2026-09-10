import {readFile,writeFile} from 'node:fs/promises';
const c=JSON.parse(await readFile('assets/vpet-catalog.json','utf8'));
const families=[...new Set(c.clips.map(x=>x.family))];
function trigger(f){
 if(f==='Default')return '自然待机';
 if(f==='StartUP')return '程序启动 / 互动面板';
 if(f==='Shutdown')return '程序退出 / 互动面板';
 if(['Eat','Drink','Gift'].includes(f))return '投喂完成后的分层动画';
 if(f.startsWith('WORK/'))return '活动计时与数值结算';
 if(f.startsWith('MOVE/'))return '移动互动 / 自主走动';
 if(f.startsWith('Raise/'))return '按住拖动 / 提起互动';
 if(f.startsWith('Switch/'))return '数值状态变化 / 互动面板';
 if(f==='LevelUP')return '升级 / 互动面板';
 if(f==='BDay')return '领养纪念日 / 庆祝按钮';
 if(f==='Music')return '播放本地音乐';
 if(f.startsWith('IDEL/')||f.startsWith('State/'))return '自主互动 / 互动面板';
 return '互动面板 / 角色触摸（适用动作）';
}
let s=`# VPet 官方默认角色覆盖清单

版本：${c.commit}。范围为官方核心默认角色，不含创意工坊或平台服务。

67 类动作，609 组动画，6,181 帧；123 种物品，13 项活动，12 套分层投喂配方。全部目录可通过动画图鉴搜索播放；缺失的状态使用该动作实际存在的平常或开心状态回退，不生成虚构素材。

| 官方动作族 | 动画组 | 帧 | 功能入口 |
| --- | ---: | ---: | --- |
`;
for(const f of families){const a=c.clips.filter(x=>x.family===f);s+=`| ${f} | ${a.length} | ${a.reduce((n,x)=>n+x.frames.length,0)} | ${trigger(f)} |\n`;}
s+=`
## 实现差异

- 活动等级门槛不阻止体验，所有 13 项活动均可启动；收益按实际时长结算。宠物金币独立于 Codex credits。
- 音乐从用户选择的本地音频播放，不录制系统声音。说话使用本地文字，不发起 AI 请求。
- 躲藏与探头在屏幕边缘播放；额度气泡与操作控件仍保持可见，可以随时停止或恢复。
- 表情可以手动选择，或由本地健康与心情自动决定。离线期间不扣状态。生日采用首次领养日期，并可随时手动庆祝。
- 图鉴中的前后分层可单独预览；吃喝与收礼功能会将角色、物品和前景合成播放。

素材与配置来源：[LorisYounger/VPet](https://github.com/LorisYounger/VPet)，许可见仓库 licenses 目录。
`;
await writeFile('docs/vpet-coverage.md',s);
console.log('Coverage table generated for '+families.length+' families.');
